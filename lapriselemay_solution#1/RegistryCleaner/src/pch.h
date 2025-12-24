// pch.h - Precompiled Header
#pragma once

// Windows headers
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#include <winreg.h>
#include <shlwapi.h>
#include <ShlObj.h>
#include <shellapi.h>

// C Runtime
#include <io.h>
#include <fcntl.h>
#include <ctime>

// C++ Standard Library
#include <string>
#include <string_view>
#include <vector>
#include <map>
#include <unordered_map>
#include <unordered_set>
#include <set>
#include <memory>
#include <optional>
#include <variant>
#include <expected>
#include <functional>
#include <algorithm>
#include <ranges>
#include <format>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <iostream>
#include <chrono>
#include <thread>
#include <mutex>
#include <atomic>
#include <span>
#include <array>
#include <concepts>
#include <type_traits>
#include <stdexcept>
#include <system_error>
#include <source_location>
#include <limits>

// Namespace aliases
namespace fs = std::filesystem;
namespace chrono = std::chrono;
namespace ranges = std::ranges;
namespace views = std::views;

// Common type aliases
using String = std::wstring;
using StringView = std::wstring_view;
using Path = fs::path;
using QWORD = unsigned __int64;
