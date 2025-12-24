// RegistryValue.cpp - Registry Value Implementation
#include "pch.h"
#include "registry/RegistryValue.h"

namespace RegistryCleaner::Registry {

    std::vector<BYTE> RegistryValue::ToBytes() const {
        return std::visit([this](auto&& arg) -> std::vector<BYTE> {
            using T = std::decay_t<decltype(arg)>;
            
            if constexpr (std::is_same_v<T, std::monostate>) {
                return {};
            }
            else if constexpr (std::is_same_v<T, String>) {
                // Include null terminator
                const auto* data = reinterpret_cast<const BYTE*>(arg.c_str());
                size_t size = (arg.size() + 1) * sizeof(wchar_t);
                return { data, data + size };
            }
            else if constexpr (std::is_same_v<T, std::vector<String>>) {
                std::vector<BYTE> result;
                for (const auto& str : arg) {
                    const auto* data = reinterpret_cast<const BYTE*>(str.c_str());
                    size_t size = (str.size() + 1) * sizeof(wchar_t);
                    result.insert(result.end(), data, data + size);
                }
                // Double null terminator
                result.push_back(0);
                result.push_back(0);
                return result;
            }
            else if constexpr (std::is_same_v<T, std::vector<BYTE>>) {
                return arg;
            }
            else if constexpr (std::is_same_v<T, DWORD>) {
                const auto* data = reinterpret_cast<const BYTE*>(&arg);
                return { data, data + sizeof(DWORD) };
            }
            else if constexpr (std::is_same_v<T, QWORD>) {
                const auto* data = reinterpret_cast<const BYTE*>(&arg);
                return { data, data + sizeof(QWORD) };
            }
            else {
                return {};
            }
        }, m_data);
    }

    RegistryValue RegistryValue::FromBytes(
        String name,
        ValueType type,
        std::span<const BYTE> data
    ) {
        ValueData valueData;

        switch (type) {
            case ValueType::None:
                valueData = std::monostate{};
                break;

            case ValueType::String:
            case ValueType::ExpandString:
            case ValueType::Link:
                if (!data.empty()) {
                    const auto* str = reinterpret_cast<const wchar_t*>(data.data());
                    size_t len = data.size() / sizeof(wchar_t);
                    // Remove null terminator if present
                    while (len > 0 && str[len - 1] == L'\0') --len;
                    valueData = String(str, len);
                } else {
                    valueData = String{};
                }
                break;

            case ValueType::MultiString:
                {
                    std::vector<String> strings;
                    const auto* ptr = reinterpret_cast<const wchar_t*>(data.data());
                    const auto* end = ptr + data.size() / sizeof(wchar_t);
                    
                    while (ptr < end && *ptr != L'\0') {
                        String str = ptr;
                        strings.push_back(str);
                        ptr += str.size() + 1;
                    }
                    valueData = std::move(strings);
                }
                break;

            case ValueType::Binary:
            case ValueType::ResourceList:
            case ValueType::FullResourceDescriptor:
            case ValueType::ResourceRequirementsList:
                valueData = std::vector<BYTE>(data.begin(), data.end());
                break;

            case ValueType::DWord:
            case ValueType::DWordBigEndian:
                if (data.size() >= sizeof(DWORD)) {
                    DWORD value = *reinterpret_cast<const DWORD*>(data.data());
                    if (type == ValueType::DWordBigEndian) {
                        value = _byteswap_ulong(value);
                    }
                    valueData = value;
                } else {
                    valueData = DWORD{ 0 };
                }
                break;

            case ValueType::QWord:
                if (data.size() >= sizeof(QWORD)) {
                    valueData = *reinterpret_cast<const QWORD*>(data.data());
                } else {
                    valueData = QWORD{ 0 };
                }
                break;

            default:
                valueData = std::vector<BYTE>(data.begin(), data.end());
                break;
        }

        return RegistryValue(std::move(name), type, std::move(valueData));
    }

} // namespace RegistryCleaner::Registry
