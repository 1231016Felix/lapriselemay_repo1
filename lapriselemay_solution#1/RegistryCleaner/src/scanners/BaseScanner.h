// BaseScanner.h - Base Scanner Interface
#pragma once

#include "pch.h"
#include "core/Config.h"
#include "registry/RegistryKey.h"

namespace RegistryCleaner::Scanners {

    using namespace Registry;
    using namespace Config;

    // Represents a single registry issue found
    struct RegistryIssue {
        String keyPath;              // Full path to the key
        String valueName;            // Name of the problematic value (empty if key issue)
        String description;          // Human-readable description
        String details;              // Additional details
        IssueCategory category;      // Category of the issue
        Severity severity;           // How serious is this issue
        bool isValueIssue;           // True if value, false if key

        [[nodiscard]] String ToString() const {
            return std::format(L"[{}] {} - {}", 
                GetSeverityName(severity),
                keyPath,
                description);
        }
    };

    // Progress callback for scan operations
    using ProgressCallback = std::function<void(StringView currentKey, size_t issuesFound)>;

    // Base class for all registry scanners
    class BaseScanner {
    public:
        explicit BaseScanner(IssueCategory category, StringView name)
            : m_category(category), m_name(name) {}

        virtual ~BaseScanner() = default;

        // Non-copyable, movable
        BaseScanner(const BaseScanner&) = delete;
        BaseScanner& operator=(const BaseScanner&) = delete;
        BaseScanner(BaseScanner&&) = default;
        BaseScanner& operator=(BaseScanner&&) = default;

        // Perform the scan and return found issues
        [[nodiscard]] virtual std::vector<RegistryIssue> Scan(
            const ProgressCallback& progress = nullptr) = 0;

        // Get scanner name
        [[nodiscard]] StringView Name() const noexcept { return m_name; }

        // Get scanner category
        [[nodiscard]] IssueCategory Category() const noexcept { return m_category; }

        // Check if scanner is enabled
        [[nodiscard]] bool IsEnabled() const noexcept { return m_enabled; }
        void SetEnabled(bool enabled) noexcept { m_enabled = enabled; }

    protected:
        // Helper to create an issue
        [[nodiscard]] RegistryIssue CreateIssue(
            StringView keyPath,
            StringView valueName,
            StringView description,
            StringView details,
            Severity severity,
            bool isValueIssue = true
        ) const {
            return RegistryIssue{
                .keyPath = String(keyPath),
                .valueName = String(valueName),
                .description = String(description),
                .details = String(details),
                .category = m_category,
                .severity = severity,
                .isValueIssue = isValueIssue
            };
        }

        // Helper to report progress
        void ReportProgress(
            const ProgressCallback& callback,
            StringView currentKey,
            size_t issuesFound
        ) const {
            if (callback) {
                callback(currentKey, issuesFound);
            }
        }

        IssueCategory m_category;
        String m_name;
        bool m_enabled = true;
    };

} // namespace RegistryCleaner::Scanners
