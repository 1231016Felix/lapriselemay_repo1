import re

file_path = r'C:\git\lapriselemay_solution#1\QuickLauncher\ViewModels\LauncherViewModel.cs'

with open(file_path, 'r', encoding='utf-8-sig') as f:
    content = f.read()

# Fix 1: Update notifications in ExecuteActionOnResult
old_notif = '''            var message = action.ActionType switch
            {
                FileActionType.CopyUrl => "URL copiée",
                FileActionType.Delete => "Envoyé à la corbeille",
                _ => null
            };'''

new_notif = '''            var message = action.ActionType switch
            {
                FileActionType.CopyUrl => "\U0001f517 URL copiée",
                FileActionType.CopyPath => "\U0001f4cb Chemin copié",
                FileActionType.CopyName => "\U0001f4cb Nom copié",
                FileActionType.Compress => "\U0001f5dc\ufe0f Archive ZIP créée",
                FileActionType.SendByEmail => "\U0001f4e7 Email en cours...",
                FileActionType.Delete => "\U0001f5d1\ufe0f Envoyé à la corbeille",
                _ => null
            };'''

content = content.replace(old_notif, new_notif)

# Fix 2: Update close-after-action list
old_close = '''            // Fermer après certaines actions
            if (action.ActionType is FileActionType.Open 
                or FileActionType.RunAsAdmin 
                or FileActionType.OpenPrivate)
            {
                _indexingService.RecordUsage(result);
                RequestHide?.Invoke(this, EventArgs.Empty);
            }
        }
        
        ShowActionsPanel = false;
    }'''

new_close = '''            // Fermer après les actions qui ouvrent quelque chose
            if (action.ActionType is FileActionType.Open 
                or FileActionType.RunAsAdmin 
                or FileActionType.OpenPrivate
                or FileActionType.OpenWith
                or FileActionType.OpenLocation
                or FileActionType.OpenInTerminal
                or FileActionType.OpenInExplorer
                or FileActionType.OpenInVSCode
                or FileActionType.EditInEditor
                or FileActionType.SendByEmail)
            {
                _indexingService.RecordUsage(result);
                RequestHide?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            if (action.ActionType == FileActionType.OpenInVSCode)
                ShowNotification?.Invoke(this, "\u274c VS Code introuvable");
        }
        
        ShowActionsPanel = false;
    }'''

content = content.replace(old_close, new_close)

with open(file_path, 'w', encoding='utf-8-sig') as f:
    f.write(content)

print("ViewModel updated successfully")
