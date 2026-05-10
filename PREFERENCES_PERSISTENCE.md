# User Preferences Persistence

## Overview
User favorites and watched items are now persisted to a CSV file and automatically restored when the user logs back in.

## How It Works

### Storage
- **File Location**: `C#/content/auth/user_preferences.csv`
- **Format**: CSV with columns: `UserId, Favorites, Watched`
- **Favorites/Watched Format**: Pipe-delimited list of story titles (e.g., `Figure: Cleopatra VII|Figure: Nefertiti`)

### Load Flow
1. User logs in via Face ID + Bluetooth
2. `StartLoginFlow()` loads the user's profile
3. `UserPreferencesManager.Load(userId)` retrieves saved preferences from CSV
4. Favorites and Watched lists are populated from the CSV
5. If no preferences exist, lists start empty (no hardcoded defaults)

### Save Flow
1. User clicks "Logout" in the circular menu
2. `HandleMenuAction("Logout", ...)` is called
3. Current Favorites and Watched lists are saved to CSV
4. User is logged out and login flow restarts

## Implementation Details

### New Class: UserPreferencesManager
- **Location**: `C#/UserPreferencesManager.cs`
- **Methods**:
  - `Load(userId)`: Retrieves preferences for a user
  - `Save(prefs)`: Saves/updates preferences for a user
  - `CsvParse()`: Handles CSV parsing with proper quote escaping
  - `CsvEscape()`: Handles CSV escaping for special characters

### New Class: UserPreferences
- **Properties**:
  - `UserId`: User identifier
  - `Favorites`: List of favorite story titles
  - `Watched`: List of watched story titles

### Changes to TuioDemo.cs
1. Added `_preferencesManager` field
2. Initialize preferences manager in constructor
3. Load preferences after successful login
4. Save preferences before logout
5. Removed hardcoded favorites from `InitializeCircularMenu()`

## Example CSV Format
```
UserId,Favorites,Watched
"user123","Figure: Cleopatra VII|Figure: Nefertiti","Figure: Tutankhamun|Connection: Husband & Wife — The Revolutionary Royal Couple"
"user456","","Figure: Ramesses II"
```

## Testing
1. Log in with Face ID
2. Add items to Favorites
3. Log out (preferences are saved)
4. Log back in with same account
5. Verify Favorites are restored

## Notes
- Empty lists are stored as empty strings in CSV
- Pipe character (`|`) is used as delimiter within the list
- Commas and quotes are properly escaped in CSV
- If a user has no saved preferences, they start with empty lists
- No hardcoded default favorites anymore
