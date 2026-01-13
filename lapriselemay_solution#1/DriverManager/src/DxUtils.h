// DirectX RAII Utilities
// Safe wrappers for DirectX resources to prevent memory leaks

#pragma once

#include <d3d11.h>
#include <utility>
#include <optional>
#include <type_traits>

namespace DriverManager {

/// <summary>
/// RAII wrapper for COM pointers (DirectX resources).
/// Automatically releases the resource when destroyed.
/// </summary>
template<typename T>
class ComPtr {
    static_assert(std::is_base_of_v<IUnknown, T>, "T must inherit from IUnknown");
    
public:
    ComPtr() noexcept : ptr_(nullptr) {}
    
    explicit ComPtr(T* ptr) noexcept : ptr_(ptr) {}
    
    ~ComPtr() { Release(); }
    
    // Move constructor
    ComPtr(ComPtr&& other) noexcept : ptr_(other.ptr_) {
        other.ptr_ = nullptr;
    }
    
    // Move assignment
    ComPtr& operator=(ComPtr&& other) noexcept {
        if (this != &other) {
            Release();
            ptr_ = other.ptr_;
            other.ptr_ = nullptr;
        }
        return *this;
    }
    
    // No copy
    ComPtr(const ComPtr&) = delete;
    ComPtr& operator=(const ComPtr&) = delete;
    
    /// <summary>
    /// Get the raw pointer
    /// </summary>
    T* Get() const noexcept { return ptr_; }
    
    /// <summary>
    /// Get pointer address for creation functions
    /// </summary>
    T** GetAddressOf() noexcept { return &ptr_; }
    
    /// <summary>
    /// Release and get pointer address for reinitialization
    /// </summary>
    T** ReleaseAndGetAddressOf() noexcept {
        Release();
        return &ptr_;
    }
    
    /// <summary>
    /// Dereference operator
    /// </summary>
    T* operator->() const noexcept { return ptr_; }
    
    /// <summary>
    /// Bool conversion
    /// </summary>
    explicit operator bool() const noexcept { return ptr_ != nullptr; }
    
    /// <summary>
    /// Release the resource
    /// </summary>
    void Release() noexcept {
        if (ptr_) {
            ptr_->Release();
            ptr_ = nullptr;
        }
    }
    
    /// <summary>
    /// Detach the pointer without releasing
    /// </summary>
    T* Detach() noexcept {
        T* temp = ptr_;
        ptr_ = nullptr;
        return temp;
    }
    
    /// <summary>
    /// Reset with a new pointer
    /// </summary>
    void Reset(T* ptr = nullptr) noexcept {
        Release();
        ptr_ = ptr;
    }
    
private:
    T* ptr_;
};

/// <summary>
/// DirectX device context holder with RAII cleanup
/// </summary>
struct D3DContext {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<IDXGISwapChain> swapChain;
    ComPtr<ID3D11RenderTargetView> renderTargetView;
    
    bool IsValid() const noexcept {
        return device && context && swapChain && renderTargetView;
    }
    
    void Cleanup() noexcept {
        renderTargetView.Release();
        swapChain.Release();
        context.Release();
        device.Release();
    }
};

/// <summary>
/// Optional wrapper for selected items (replaces raw pointers)
/// </summary>
template<typename T>
using OptionalRef = std::optional<std::reference_wrapper<T>>;

/// <summary>
/// RAII handle wrapper for Windows handles
/// </summary>
template<typename HandleType, typename Deleter>
class UniqueHandle {
public:
    UniqueHandle() noexcept : handle_(INVALID_HANDLE_VALUE) {}
    
    explicit UniqueHandle(HandleType handle) noexcept : handle_(handle) {}
    
    ~UniqueHandle() { Close(); }
    
    // Move only
    UniqueHandle(UniqueHandle&& other) noexcept : handle_(other.handle_) {
        other.handle_ = INVALID_HANDLE_VALUE;
    }
    
    UniqueHandle& operator=(UniqueHandle&& other) noexcept {
        if (this != &other) {
            Close();
            handle_ = other.handle_;
            other.handle_ = INVALID_HANDLE_VALUE;
        }
        return *this;
    }
    
    UniqueHandle(const UniqueHandle&) = delete;
    UniqueHandle& operator=(const UniqueHandle&) = delete;
    
    HandleType Get() const noexcept { return handle_; }
    HandleType* GetAddressOf() noexcept { return &handle_; }
    
    explicit operator bool() const noexcept {
        return handle_ != INVALID_HANDLE_VALUE && handle_ != nullptr;
    }
    
    void Close() noexcept {
        if (*this) {
            Deleter()(handle_);
            handle_ = INVALID_HANDLE_VALUE;
        }
    }
    
    HandleType Release() noexcept {
        HandleType temp = handle_;
        handle_ = INVALID_HANDLE_VALUE;
        return temp;
    }
    
private:
    HandleType handle_;
};

// Common handle deleters
struct HandleDeleter {
    void operator()(HANDLE h) const noexcept {
        if (h != INVALID_HANDLE_VALUE && h != nullptr) {
            CloseHandle(h);
        }
    }
};

// Typedefs for common handle types
using UniqueFileHandle = UniqueHandle<HANDLE, HandleDeleter>;

} // namespace DriverManager
