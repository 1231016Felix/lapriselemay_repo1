// RegistryValue.h - Registry Value Types and Utilities
#pragma once

#include "pch.h"

namespace RegistryCleaner::Registry {

    // Registry value types
    enum class ValueType : DWORD {
        None = REG_NONE,
        String = REG_SZ,
        ExpandString = REG_EXPAND_SZ,
        Binary = REG_BINARY,
        DWord = REG_DWORD,
        DWordBigEndian = REG_DWORD_BIG_ENDIAN,
        Link = REG_LINK,
        MultiString = REG_MULTI_SZ,
        ResourceList = REG_RESOURCE_LIST,
        FullResourceDescriptor = REG_FULL_RESOURCE_DESCRIPTOR,
        ResourceRequirementsList = REG_RESOURCE_REQUIREMENTS_LIST,
        QWord = REG_QWORD
    };

    // Get type name
    [[nodiscard]] inline String GetTypeName(ValueType type) {
        switch (type) {
            case ValueType::None:                      return L"REG_NONE";
            case ValueType::String:                    return L"REG_SZ";
            case ValueType::ExpandString:              return L"REG_EXPAND_SZ";
            case ValueType::Binary:                    return L"REG_BINARY";
            case ValueType::DWord:                     return L"REG_DWORD";
            case ValueType::DWordBigEndian:            return L"REG_DWORD_BIG_ENDIAN";
            case ValueType::Link:                      return L"REG_LINK";
            case ValueType::MultiString:               return L"REG_MULTI_SZ";
            case ValueType::ResourceList:              return L"REG_RESOURCE_LIST";
            case ValueType::FullResourceDescriptor:    return L"REG_FULL_RESOURCE_DESCRIPTOR";
            case ValueType::ResourceRequirementsList:  return L"REG_RESOURCE_REQUIREMENTS_LIST";
            case ValueType::QWord:                     return L"REG_QWORD";
            default:                                   return L"UNKNOWN";
        }
    }

    // Registry value data variant
    using ValueData = std::variant<
        std::monostate,              // None
        String,                      // String, ExpandString
        std::vector<String>,         // MultiString
        std::vector<BYTE>,           // Binary
        DWORD,                       // DWord
        QWORD                        // QWord
    >;

    // Registry value class
    class RegistryValue {
    public:
        RegistryValue() = default;

        RegistryValue(String name, ValueType type, ValueData data)
            : m_name(std::move(name))
            , m_type(type)
            , m_data(std::move(data)) {}

        // Getters
        [[nodiscard]] const String& Name() const noexcept { return m_name; }
        [[nodiscard]] ValueType Type() const noexcept { return m_type; }
        [[nodiscard]] const ValueData& Data() const noexcept { return m_data; }

        // Setters
        void SetName(String name) { m_name = std::move(name); }
        void SetType(ValueType type) { m_type = type; }
        void SetData(ValueData data) { m_data = std::move(data); }

        // Type checks
        [[nodiscard]] bool IsString() const noexcept {
            return m_type == ValueType::String || m_type == ValueType::ExpandString;
        }
        [[nodiscard]] bool IsMultiString() const noexcept { return m_type == ValueType::MultiString; }
        [[nodiscard]] bool IsBinary() const noexcept { return m_type == ValueType::Binary; }
        [[nodiscard]] bool IsDWord() const noexcept { return m_type == ValueType::DWord; }
        [[nodiscard]] bool IsQWord() const noexcept { return m_type == ValueType::QWord; }

        // Get as specific type (throws if wrong type)
        [[nodiscard]] const String& AsString() const {
            if (!std::holds_alternative<String>(m_data)) {
                throw std::bad_variant_access();
            }
            return std::get<String>(m_data);
        }

        [[nodiscard]] const std::vector<String>& AsMultiString() const {
            if (!std::holds_alternative<std::vector<String>>(m_data)) {
                throw std::bad_variant_access();
            }
            return std::get<std::vector<String>>(m_data);
        }

        [[nodiscard]] const std::vector<BYTE>& AsBinary() const {
            if (!std::holds_alternative<std::vector<BYTE>>(m_data)) {
                throw std::bad_variant_access();
            }
            return std::get<std::vector<BYTE>>(m_data);
        }

        [[nodiscard]] DWORD AsDWord() const {
            if (!std::holds_alternative<DWORD>(m_data)) {
                throw std::bad_variant_access();
            }
            return std::get<DWORD>(m_data);
        }

        [[nodiscard]] QWORD AsQWord() const {
            if (!std::holds_alternative<QWORD>(m_data)) {
                throw std::bad_variant_access();
            }
            return std::get<QWORD>(m_data);
        }

        // Try to get as specific type (returns nullopt if wrong type)
        [[nodiscard]] std::optional<String> TryAsString() const {
            if (std::holds_alternative<String>(m_data)) {
                return std::get<String>(m_data);
            }
            return std::nullopt;
        }

        [[nodiscard]] std::optional<DWORD> TryAsDWord() const {
            if (std::holds_alternative<DWORD>(m_data)) {
                return std::get<DWORD>(m_data);
            }
            return std::nullopt;
        }

        // Get string representation of value
        [[nodiscard]] String ToString() const {
            return std::visit([this](auto&& arg) -> String {
                using T = std::decay_t<decltype(arg)>;
                if constexpr (std::is_same_v<T, std::monostate>) {
                    return L"(empty)";
                } else if constexpr (std::is_same_v<T, String>) {
                    return arg;
                } else if constexpr (std::is_same_v<T, std::vector<String>>) {
                    String result;
                    for (size_t i = 0; i < arg.size(); ++i) {
                        result += arg[i];
                        if (i < arg.size() - 1) result += L"; ";
                    }
                    return result;
                } else if constexpr (std::is_same_v<T, std::vector<BYTE>>) {
                    return std::format(L"(binary data, {} bytes)", arg.size());
                } else if constexpr (std::is_same_v<T, DWORD>) {
                    return std::format(L"{}", arg);
                } else if constexpr (std::is_same_v<T, QWORD>) {
                    return std::format(L"{}", arg);
                } else {
                    return L"(unknown)";
                }
            }, m_data);
        }

        // Convert to raw bytes for writing to registry
        [[nodiscard]] std::vector<BYTE> ToBytes() const;

        // Create from raw bytes
        [[nodiscard]] static RegistryValue FromBytes(
            String name,
            ValueType type,
            std::span<const BYTE> data
        );

    private:
        String m_name;
        ValueType m_type = ValueType::None;
        ValueData m_data;
    };

} // namespace RegistryCleaner::Registry
