#pragma once

#include <Windows.h>
#include <string>
#include <optional>
#include <variant>
#include <functional>
#include <stdexcept>

namespace DriverManager {

/// <summary>
/// Représente une erreur avec code et message
/// </summary>
struct Error {
    DWORD code = 0;
    std::wstring message;
    
    Error() = default;
    Error(DWORD c, std::wstring msg) : code(c), message(std::move(msg)) {}
    explicit Error(const std::wstring& msg) : code(0), message(msg) {}
    
    [[nodiscard]] bool HasCode() const noexcept { return code != 0; }
    
    [[nodiscard]] std::wstring ToString() const {
        if (code != 0) {
            return L"[" + std::to_wstring(code) + L"] " + message;
        }
        return message;
    }
};

/// <summary>
/// Résultat d'une opération qui peut réussir ou échouer.
/// Utilise std::variant pour un stockage efficace sans overhead d'allocation.
/// </summary>
template<typename T>
class Result {
public:
    static Result Success(T value) {
        Result r;
        r.m_data = std::move(value);
        return r;
    }
    
    static Result Failure(const std::wstring& message, DWORD code = 0) {
        Result r;
        r.m_data = Error(code, message);
        return r;
    }
    
    static Result FailureFromLastError(const std::wstring& context = L"") {
        DWORD code = GetLastError();
        wchar_t buffer[256] = {0};
        FormatMessageW(
            FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr, code, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            buffer, 256, nullptr);
        std::wstring msg = context.empty() ? buffer : context + L": " + buffer;
        return Failure(msg, code);
    }

    [[nodiscard]] bool IsSuccess() const noexcept {
        return std::holds_alternative<T>(m_data);
    }
    
    [[nodiscard]] bool IsFailure() const noexcept {
        return std::holds_alternative<Error>(m_data);
    }
    
    [[nodiscard]] explicit operator bool() const noexcept {
        return IsSuccess();
    }
    
    [[nodiscard]] const T& Value() const& {
        if (IsFailure()) {
            throw std::runtime_error("Attempted to access value of failed result");
        }
        return std::get<T>(m_data);
    }
    
    [[nodiscard]] T&& Value() && {
        if (IsFailure()) {
            throw std::runtime_error("Attempted to access value of failed result");
        }
        return std::get<T>(std::move(m_data));
    }
    
    [[nodiscard]] T ValueOr(T defaultValue) const {
        return IsSuccess() ? std::get<T>(m_data) : std::move(defaultValue);
    }
    
    [[nodiscard]] const Error& GetError() const& {
        if (IsSuccess()) {
            throw std::runtime_error("Attempted to access error of successful result");
        }
        return std::get<Error>(m_data);
    }
    
    [[nodiscard]] std::wstring ErrorMessage() const {
        return IsFailure() ? std::get<Error>(m_data).message : L"";
    }
    
    [[nodiscard]] DWORD ErrorCode() const noexcept {
        return IsFailure() ? std::get<Error>(m_data).code : 0;
    }
    
    template<typename FSuccess, typename FFailure>
    auto Match(FSuccess onSuccess, FFailure onFailure) const
        -> decltype(onSuccess(std::declval<T>())) {
        if (IsSuccess()) {
            return onSuccess(std::get<T>(m_data));
        }
        return onFailure(std::get<Error>(m_data));
    }
    
    template<typename F>
    auto Map(F&& f) const -> Result<decltype(f(std::declval<T>()))> {
        using U = decltype(f(std::declval<T>()));
        if (IsSuccess()) {
            return Result<U>::Success(f(std::get<T>(m_data)));
        }
        return Result<U>::Failure(GetError().message, GetError().code);
    }
    
    template<typename F>
    auto AndThen(F&& f) const -> decltype(f(std::declval<T>())) {
        if (IsSuccess()) {
            return f(std::get<T>(m_data));
        }
        using ReturnType = decltype(f(std::declval<T>()));
        return ReturnType::Failure(GetError().message, GetError().code);
    }
    
    Result& OnSuccess(const std::function<void(const T&)>& action) {
        if (IsSuccess()) {
            action(std::get<T>(m_data));
        }
        return *this;
    }
    
    Result& OnFailure(const std::function<void(const Error&)>& action) {
        if (IsFailure()) {
            action(std::get<Error>(m_data));
        }
        return *this;
    }

private:
    std::variant<T, Error> m_data;
};

/// <summary>
/// Spécialisation pour void
/// </summary>
template<>
class Result<void> {
public:
    static Result Success() {
        Result r;
        r.m_success = true;
        return r;
    }
    
    static Result Failure(const std::wstring& message, DWORD code = 0) {
        Result r;
        r.m_success = false;
        r.m_error = Error(code, message);
        return r;
    }
    
    static Result FailureFromLastError(const std::wstring& context = L"") {
        DWORD code = GetLastError();
        wchar_t buffer[256] = {0};
        FormatMessageW(
            FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr, code, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            buffer, 256, nullptr);
        std::wstring msg = context.empty() ? buffer : context + L": " + buffer;
        return Failure(msg, code);
    }
    
    [[nodiscard]] bool IsSuccess() const noexcept { return m_success; }
    [[nodiscard]] bool IsFailure() const noexcept { return !m_success; }
    [[nodiscard]] explicit operator bool() const noexcept { return m_success; }
    
    [[nodiscard]] const Error& GetError() const& {
        if (m_success) {
            throw std::runtime_error("Attempted to access error of successful result");
        }
        return m_error;
    }
    
    [[nodiscard]] std::wstring ErrorMessage() const {
        return m_success ? L"" : m_error.message;
    }
    
    [[nodiscard]] DWORD ErrorCode() const noexcept {
        return m_success ? 0 : m_error.code;
    }
    
    Result& OnSuccess(const std::function<void()>& action) {
        if (m_success) action();
        return *this;
    }
    
    Result& OnFailure(const std::function<void(const Error&)>& action) {
        if (!m_success) action(m_error);
        return *this;
    }

private:
    bool m_success = false;
    Error m_error;
};

using VoidResult = Result<void>;

namespace Results {
    inline VoidResult Ok() { return VoidResult::Success(); }
    
    template<typename T>
    Result<T> Ok(T value) { return Result<T>::Success(std::move(value)); }
    
    inline VoidResult Fail(const std::wstring& msg, DWORD code = 0) {
        return VoidResult::Failure(msg, code);
    }
    
    template<typename T>
    Result<T> Fail(const std::wstring& msg, DWORD code = 0) {
        return Result<T>::Failure(msg, code);
    }
    
    inline VoidResult FromLastError(const std::wstring& context = L"") {
        return VoidResult::FailureFromLastError(context);
    }
    
    inline VoidResult FailureFromLastError(const std::wstring& context = L"") {
        return VoidResult::FailureFromLastError(context);
    }
    
    template<typename T>
    Result<T> FromLastError(const std::wstring& context = L"") {
        return Result<T>::FailureFromLastError(context);
    }
}

} // namespace DriverManager
