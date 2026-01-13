using System.Text.Json.Serialization;

namespace CleanUninstaller.Models;

/// <summary>
/// Représente un snapshot complet de l'état du système à un instant T
/// Utilisé pour la comparaison avant/après installation
/// </summary>
public class InstallationSnapshot
{
    /// <summary>
    /// Identifiant unique du snapshot
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Date/heure de création du snapshot
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>
    /// Type de snapshot (Before/After)
    /// </summary>
    public SnapshotType Type { get; init; }

    /// <summary>
    /// Nom de l'installation associée (optionnel)
    /// </summary>
    public string? InstallationName { get; set; }

    /// <summary>
    /// Liste des fichiers présents dans les dossiers surveillés
    /// </summary>
    public HashSet<FileSnapshot> Files { get; init; } = [];

    /// <summary>
    /// Liste des clés de registre surveillées
    /// </summary>
    public HashSet<RegistrySnapshot> RegistryKeys { get; init; } = [];

    /// <summary>
    /// Liste des services Windows
    /// </summary>
    public HashSet<ServiceSnapshot> Services { get; init; } = [];

    /// <summary>
    /// Liste des tâches planifiées
    /// </summary>
    public HashSet<ScheduledTaskSnapshot> ScheduledTasks { get; init; } = [];

    /// <summary>
    /// Liste des règles de pare-feu
    /// </summary>
    public HashSet<FirewallRuleSnapshot> FirewallRules { get; init; } = [];

    /// <summary>
    /// Variables d'environnement système
    /// </summary>
    public Dictionary<string, string> SystemEnvironmentVariables { get; init; } = [];

    /// <summary>
    /// Variables d'environnement utilisateur
    /// </summary>
    public Dictionary<string, string> UserEnvironmentVariables { get; init; } = [];

    /// <summary>
    /// Entrées de démarrage automatique
    /// </summary>
    public HashSet<StartupEntrySnapshot> StartupEntries { get; init; } = [];

    /// <summary>
    /// Pilotes système installés
    /// </summary>
    public HashSet<DriverSnapshot> Drivers { get; init; } = [];

    /// <summary>
    /// Objets COM enregistrés
    /// </summary>
    public HashSet<ComObjectSnapshot> ComObjects { get; init; } = [];

    /// <summary>
    /// Associations de fichiers
    /// </summary>
    public Dictionary<string, FileAssociationSnapshot> FileAssociations { get; init; } = [];

    /// <summary>
    /// Polices installées
    /// </summary>
    public HashSet<FontSnapshot> InstalledFonts { get; init; } = [];

    /// <summary>
    /// Shell extensions enregistrées
    /// </summary>
    public HashSet<ShellExtensionSnapshot> ShellExtensions { get; init; } = [];

    /// <summary>
    /// Statistiques du snapshot
    /// </summary>
    [JsonIgnore]
    public SnapshotStatistics Statistics => new()
    {
        FileCount = Files.Count,
        RegistryKeyCount = RegistryKeys.Count,
        ServiceCount = Services.Count,
        ScheduledTaskCount = ScheduledTasks.Count,
        FirewallRuleCount = FirewallRules.Count,
        StartupEntryCount = StartupEntries.Count,
        DriverCount = Drivers.Count,
        ComObjectCount = ComObjects.Count,
        FileAssociationCount = FileAssociations.Count,
        FontCount = InstalledFonts.Count,
        ShellExtensionCount = ShellExtensions.Count,
        TotalSize = Files.Sum(f => f.Size)
    };
}

/// <summary>
/// Type de snapshot
/// </summary>
public enum SnapshotType
{
    Before,
    After,
    Baseline,
    Manual
}

/// <summary>
/// Statistiques d'un snapshot
/// </summary>
public class SnapshotStatistics
{
    public int FileCount { get; init; }
    public int RegistryKeyCount { get; init; }
    public int ServiceCount { get; init; }
    public int ScheduledTaskCount { get; init; }
    public int FirewallRuleCount { get; init; }
    public int StartupEntryCount { get; init; }
    public int DriverCount { get; init; }
    public int ComObjectCount { get; init; }
    public int FileAssociationCount { get; init; }
    public int FontCount { get; init; }
    public int ShellExtensionCount { get; init; }
    public long TotalSize { get; init; }
}

/// <summary>
/// Snapshot d'un fichier
/// </summary>
public record FileSnapshot
{
    public required string Path { get; init; }
    public long Size { get; init; }
    public DateTime LastModified { get; init; }
    public string? Hash { get; init; }

    public virtual bool Equals(FileSnapshot? other) =>
        other is not null && Path.Equals(other.Path, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        Path.ToLowerInvariant().GetHashCode();
}

/// <summary>
/// Snapshot d'une clé/valeur de registre
/// </summary>
public record RegistrySnapshot
{
    public required string Path { get; init; }
    public string? ValueName { get; init; }
    public object? Value { get; init; }
    public Microsoft.Win32.RegistryValueKind ValueKind { get; init; }
    public bool IsKey { get; init; }

    public virtual bool Equals(RegistrySnapshot? other) =>
        other is not null &&
        Path.Equals(other.Path, StringComparison.OrdinalIgnoreCase) &&
        (ValueName?.Equals(other.ValueName, StringComparison.OrdinalIgnoreCase) ?? other.ValueName == null);

    public override int GetHashCode() =>
        HashCode.Combine(Path.ToLowerInvariant(), ValueName?.ToLowerInvariant());
}

/// <summary>
/// Snapshot d'un service Windows
/// </summary>
public record ServiceSnapshot
{
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public string? ImagePath { get; init; }
    public string? Description { get; init; }
    public int StartType { get; init; }

    public virtual bool Equals(ServiceSnapshot? other) =>
        other is not null && Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        Name.ToLowerInvariant().GetHashCode();
}

/// <summary>
/// Snapshot d'une tâche planifiée
/// </summary>
public record ScheduledTaskSnapshot
{
    public required string Path { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? ActionPath { get; init; }

    public virtual bool Equals(ScheduledTaskSnapshot? other) =>
        other is not null && Path.Equals(other.Path, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        Path.ToLowerInvariant().GetHashCode();
}

/// <summary>
/// Snapshot d'une règle de pare-feu
/// </summary>
public record FirewallRuleSnapshot
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? ApplicationPath { get; init; }
    public int Direction { get; init; }
    public int Action { get; init; }
    public bool Enabled { get; init; }

    public virtual bool Equals(FirewallRuleSnapshot? other) =>
        other is not null && Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        Name.ToLowerInvariant().GetHashCode();
}

/// <summary>
/// Snapshot d'une entrée de démarrage
/// </summary>
public record StartupEntrySnapshot
{
    public required string Name { get; init; }
    public required string Location { get; init; }
    public string? Command { get; init; }

    public virtual bool Equals(StartupEntrySnapshot? other) =>
        other is not null &&
        Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase) &&
        Location.Equals(other.Location, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(Name.ToLowerInvariant(), Location.ToLowerInvariant());
}

/// <summary>
/// Snapshot d'un pilote système
/// </summary>
public record DriverSnapshot
{
    public required string Name { get; init; }
    public string? ImagePath { get; init; }
    public string? Description { get; init; }
    public int Type { get; init; }

    public virtual bool Equals(DriverSnapshot? other) =>
        other is not null && Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => Name.ToLowerInvariant().GetHashCode();
}

/// <summary>
/// Snapshot d'un objet COM
/// </summary>
public record ComObjectSnapshot
{
    public required string CLSID { get; init; }
    public string? Name { get; init; }
    public string? ServerPath { get; init; }

    public virtual bool Equals(ComObjectSnapshot? other) =>
        other is not null && CLSID.Equals(other.CLSID, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => CLSID.ToLowerInvariant().GetHashCode();
}

/// <summary>
/// Snapshot d'une association de fichier
/// </summary>
public record FileAssociationSnapshot
{
    public required string Extension { get; init; }
    public required string ProgId { get; init; }
}

/// <summary>
/// Snapshot d'une police installée
/// </summary>
public record FontSnapshot
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }

    public virtual bool Equals(FontSnapshot? other) =>
        other is not null && Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => Name.ToLowerInvariant().GetHashCode();
}

/// <summary>
/// Snapshot d'une shell extension
/// </summary>
public record ShellExtensionSnapshot
{
    public required string Name { get; init; }
    public required string Type { get; init; } // ContextMenu, PropertySheet, etc.
    public string? CLSID { get; init; }
    public string? Description { get; init; }

    public virtual bool Equals(ShellExtensionSnapshot? other) =>
        other is not null && 
        Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase) &&
        Type.Equals(other.Type, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => 
        HashCode.Combine(Name.ToLowerInvariant(), Type.ToLowerInvariant());
}
