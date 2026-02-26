# Offline Network Manager

![Banner](Assets/Sprites/PF_OfflineNetworkManager.png)

**Offline Network Manager for Unity**

A robust singleton manager for detecting and handling internet connectivity changes in Unity applications. Features adaptive check intervals that automatically adjust based on connection duration to optimize battery life and responsiveness.

## Features

- **Adaptive Interval Checking** - More frequent when connection state changes, less when stable
- **Automatic Reconnection Detection** - Built-in retry logic with configurable debounce
- **Event-Driven Architecture** - Easy integration with existing systems
- **Offline Data Backup/Restore** - Built-in PlayerPrefs integration for offline persistence
- **Inspector Configuration** - All settings editable in Unity Inspector
- **Scene Persistence** - DontDestroyOnLoad for seamless scene transitions
- **Battery Optimization** - Reduces check frequency for stable long-running connections
- **Unity Events Support** - Direct UI integration without code

## Installation

1. Copy the `OfflineNetworkManager` folder to your project's `Assets/Plugins/DryreLHub/` directory
2. Unity will automatically recognize the plugin and configure it for all platforms
3. The manager initializes automatically - no manual setup required

## Quick Start

### Basic Usage

```csharp
using UnityEngine;

public class NetworkExample : MonoBehaviour
{
    void Start()
    {
        // Subscribe to connectivity changes
        OfflineNetworkManager.Instance.OnInternetConnectivityChanged += HandleConnectivityChange;
    }

    void HandleConnectivityChange(bool isOnline)
    {
        if (isOnline)
        {
            Debug.Log("Connected to internet!");
            // Resume network operations
        }
        else
        {
            Debug.Log("Lost internet connection");
            // Handle offline mode
        }
    }

    void OnDestroy()
    {
        // Always unsubscribe from events
        OfflineNetworkManager.Instance.OnInternetConnectivityChanged -= HandleConnectivityChange;
    }
}
```

### Making Network Requests

```csharp
public void SaveData()
{
    // Always check before making network requests
    if (!OfflineNetworkManager.Instance.CanAttemptSync())
    {
        Debug.LogWarning("Offline - data will be queued for sync");
        return;
    }

    // Make your network request
    StartCoroutine(SendDataToServer());
}

IEnumerator SendDataToServer()
{
    // Your network request code here
    UnityWebRequest request = UnityWebRequest.Post("https://api.example.com/data", formData);
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
    {
        // Mark sync as successful
        OfflineNetworkManager.Instance.MarkSyncSucceeded();
        Debug.Log("Data saved successfully!");
    }
    else
    {
        // Mark sync as failed - will trigger retry logic
        OfflineNetworkManager.Instance.MarkSyncFailed();
        Debug.LogError("Failed to save data - will retry when online");
    }
}
```

### Handling Sync Retries

```csharp
void Start()
{
    // Subscribe to retry events
    OfflineNetworkManager.Instance.OnSyncRetryReady += HandleSyncRetry;
}

void HandleSyncRetry()
{
    Debug.Log("Ready to retry sync operation");
    // Retry your failed network operation
    SaveData();
}
```

## Configuration

All settings are configurable in the Unity Inspector:

### Adaptive Check Intervals

The manager automatically adjusts check frequency based on connection duration:

| Connection Duration | Default Interval | Purpose |
|-------------------|------------------|---------|
| First 1 minute | 5 seconds | Quick detection of state changes |
| 1-10 minutes | 10 seconds | Moderate monitoring |
| 10-60 minutes | 30 seconds | Reduced frequency for stable connections |
| 1-10 hours | 10 minutes | Low frequency monitoring |
| 10+ hours | 1 hour | Minimal checks for battery conservation |

### Inspector Settings

- **Enable Offline Detection** - Toggle automatic connectivity monitoring
- **Check Interval Settings** - Customize each adaptive interval period
- **Sync Retry Debounce** - Minimum delay before retrying failed syncs (default: 60s)
- **Show Network Warnings** - Display warning messages for network issues
- **Enable Debug Logs** - Detailed logging for troubleshooting

## API Reference

### Properties

#### `OfflineNetworkManager.Instance`
Singleton instance - auto-creates if not present in scene.

```csharp
var manager = OfflineNetworkManager.Instance;
```

### Methods

#### `IsOnline()`
Check if device currently has internet connectivity.

```csharp
bool isConnected = OfflineNetworkManager.Instance.IsOnline();
```

#### `CanAttemptSync()`
Check if a sync attempt can be made (guards against offline spam). Always call before network requests.

```csharp
if (OfflineNetworkManager.Instance.CanAttemptSync())
{
    // Safe to make network request
}
```

#### `MarkSyncSucceeded()`
Mark a sync operation as successful. Clears pending sync flag.

```csharp
OfflineNetworkManager.Instance.MarkSyncSucceeded();
```

#### `MarkSyncFailed()`
Mark a sync operation as failed. Sets offline state and schedules retry.

```csharp
OfflineNetworkManager.Instance.MarkSyncFailed();
```

#### `HasPendingSyncData()`
Check if there's data waiting to be synced when connection is restored.

```csharp
bool hasPending = OfflineNetworkManager.Instance.HasPendingSyncData();
```

#### `GetNetworkStatus()`
Get detailed network status including sync state.

```csharp
NetworkStatus status = OfflineNetworkManager.Instance.GetNetworkStatus();
// Returns: Online, OfflinePending, or OfflineNoData
```

#### `GetRetryCountdown()`
Get time remaining until next retry attempt for failed syncs.

```csharp
float seconds = OfflineNetworkManager.Instance.GetRetryCountdown();
```

#### `ForceSyncIfOnline()`
Force immediate sync retry if online, bypassing debounce timer. Useful for user-initiated retry buttons.

```csharp
// Called by UI button
public void OnRetryButtonClick()
{
    OfflineNetworkManager.Instance.ForceSyncIfOnline();
}
```

#### `SetSyncRetryDebounce(float seconds)`
Update retry debounce duration at runtime (minimum 5s).

```csharp
OfflineNetworkManager.Instance.SetSyncRetryDebounce(120f); // 2 minutes
```

#### `CheckInternetConnectionNow()`
Force an immediate connectivity check, ignoring adaptive intervals.

```csharp
OfflineNetworkManager.Instance.CheckInternetConnectionNow();
```

#### `SaveOfflineDataBackup(Dictionary<string, int> data, string backupKey, string userIdKey, string userId)`
Save data to PlayerPrefs for offline persistence.

```csharp
var playerData = new Dictionary<string, int>
{
    { "level", 5 },
    { "coins", 1000 }
};
OfflineNetworkManager.Instance.SaveOfflineDataBackup(
    playerData, 
    "gameData_backup", 
    "userId_backup",
    currentUserId
);
```

#### `RestoreOfflineDataBackup(string backupKey, string userIdKey, string userId)`
Restore previously saved offline data from PlayerPrefs.

```csharp
var restoredData = OfflineNetworkManager.Instance.RestoreOfflineDataBackup(
    "gameData_backup",
    "userId_backup", 
    currentUserId
);
```

#### `ShouldRetryRequest(long responseCode, string errorType)`
Determine if a failed request should be retried based on error type.

```csharp
if (OfflineNetworkManager.Instance.ShouldRetryRequest(request.responseCode, "NetworkError"))
{
    // Retry the request
}
```

### Events

#### `OnInternetConnectivityChanged`
Fired when internet connectivity changes. Parameter: `true` = online, `false` = offline.

```csharp
OfflineNetworkManager.Instance.OnInternetConnectivityChanged += (isOnline) => {
    Debug.Log($"Internet is now: {(isOnline ? "Online" : "Offline")}");
};
```

#### `OnNetworkStatusChanged`
Fired when network status changes (Online/OfflinePending/OfflineNoData).

```csharp
OfflineNetworkManager.Instance.OnNetworkStatusChanged += (status) => {
    switch (status)
    {
        case NetworkStatus.Online:
            Debug.Log("Connected and ready");
            break;
        case NetworkStatus.OfflinePending:
            Debug.Log("Offline with pending sync data");
            break;
        case NetworkStatus.OfflineNoData:
            Debug.Log("Offline, no pending data");
            break;
    }
};
```

#### `OnSyncRetryReady`
Fired when a failed sync is ready to be retried (after debounce period).

```csharp
OfflineNetworkManager.Instance.OnSyncRetryReady += () => {
    Debug.Log("Ready to retry failed sync operation");
    RetrySyncOperation();
};
```

### Unity Events (Inspector)

For UI integration without code, use these UnityEvents in the Inspector:

- **OnConnectionLostEvent** - Called when internet connection is lost
- **OnConnectionRestoredEvent** - Called when connection is restored
- **OnReconnectingEvent** - Called when attempting to reconnect
- **OnSyncRequiredEvent** - Called when sync data is ready

#### Example: Show/Hide Offline Panel

1. Add `OfflineNetworkManager` to your scene
2. In Inspector, expand the **Events** section
3. Add your UI Panel's `GameObject.SetActive` to `OnConnectionLostEvent`
4. Set the boolean parameter to `true`
5. Add the same Panel's `SetActive` to `OnConnectionRestoredEvent` with `false`

## Advanced Usage

### Custom Initialization

```csharp
void Start()
{
    OfflineNetworkManager.Instance.Initialize(
        enableDetection: true,
        checkInterval: 0f,        // Ignored - use Inspector intervals
        retryDebounce: 120f,      // 2 minutes between retries
        debugLogs: true,          // Enable detailed logging
        warnings: true            // Show warning messages
    );
}
```

### Network Status UI

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NetworkStatusUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Image statusIcon;
    [SerializeField] private GameObject offlinePanel;
    [SerializeField] private Button retryButton;

    void Start()
    {
        // Subscribe to events
        OfflineNetworkManager.Instance.OnInternetConnectivityChanged += UpdateUI;
        
        // Setup retry button
        retryButton.onClick.AddListener(() => {
            OfflineNetworkManager.Instance.ForceSyncIfOnline();
        });
        
        // Initial update
        UpdateUI(OfflineNetworkManager.Instance.IsOnline());
    }

    void UpdateUI(bool isOnline)
    {
        if (isOnline)
        {
            statusText.text = "Online";
            statusIcon.color = Color.green;
            offlinePanel.SetActive(false);
        }
        else
        {
            statusText.text = "Offline";
            statusIcon.color = Color.red;
            offlinePanel.SetActive(true);
        }
    }

    void OnDestroy()
    {
        OfflineNetworkManager.Instance.OnInternetConnectivityChanged -= UpdateUI;
    }
}
```

### Countdown Timer for Retry

```csharp
using UnityEngine;
using TMPro;

public class RetryCountdown : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI countdownText;

    void Update()
    {
        float countdown = OfflineNetworkManager.Instance.GetRetryCountdown();
        
        if (countdown > 0)
        {
            countdownText.text = $"Retry in: {Mathf.CeilToInt(countdown)}s";
        }
        else if (OfflineNetworkManager.Instance.HasPendingSyncData())
        {
            countdownText.text = "Ready to retry";
        }
        else
        {
            countdownText.text = "";
        }
    }
}
```

## Best Practices

1. **Always Check Before Requests**: Use `CanAttemptSync()` before making network requests
2. **Mark Sync Results**: Always call `MarkSyncSucceeded()` or `MarkSyncFailed()` after requests
3. **Unsubscribe from Events**: Always unsubscribe in `OnDestroy()` to prevent memory leaks
4. **Use Adaptive Intervals**: Don't override the adaptive intervals unless necessary
5. **Handle Retries**: Subscribe to `OnSyncRetryReady` to handle automatic retries
6. **User Feedback**: Show clear offline indicators in your UI
7. **Backup Important Data**: Use `SaveOfflineDataBackup()` for critical data

## Performance Considerations

- **Battery Friendly**: Adaptive intervals reduce checks for long-stable connections
- **Minimal Overhead**: Singleton pattern ensures single instance across scenes
- **Automatic Cleanup**: Events and resources cleaned up properly on destroy
- **Scene Persistent**: Uses `DontDestroyOnLoad` for seamless scene transitions

## Platform Support

Works on all Unity platforms:
- ✅ Windows (Standalone)
- ✅ macOS (Standalone)
- ✅ Linux (Standalone)
- ✅ WebGL
- ✅ Android
- ✅ iOS
- ✅ Console Platforms

## Dependencies

- **Unity 2021.2+** (or newer)
- **Newtonsoft.Json** (for offline data serialization)

## Troubleshooting

### Connection not detected
- Enable **Debug Logs** in Inspector to see connectivity checks
- Verify `enableOfflineDetection` is enabled
- Check that `Application.internetReachability` works on your platform

### Events not firing
- Ensure you're subscribing to events **after** getting the Instance
- Check for null references in event handlers
- Verify you're not unsubscribing too early

### Retries not working
- Confirm `MarkSyncFailed()` is called when requests fail
- Check `syncRetryDebounceSeconds` is not too high
- Subscribe to `OnSyncRetryReady` event

## License

MIT License - Part of the DryreLHub Unity Plugins collection

## Version History

- **1.0.0** - Initial release
  - Adaptive interval checking
  - Event-driven architecture
  - Offline data backup/restore
  - Unity Events for UI integration
  - Cross-platform support

## Author

**DryreL Hub**

## Support

For issues, feature requests, or questions, please visit the GitHub repository or contact the author.
