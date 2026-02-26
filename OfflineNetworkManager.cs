/*
 * OfflineNetworkManager - Adaptive Internet Connectivity Manager for Unity
 * 
 * Author: DryreL Hub
 * Version: 1.0.0
 * License: MIT
 * 
 * Description:
 *   A robust singleton manager for detecting and handling internet connectivity changes
 *   in Unity applications. Features adaptive check intervals that automatically adjust
 *   based on connection duration to optimize battery life and responsiveness.
 * 
 * Key Features:
 *   - Adaptive interval checking (more frequent when connection state changes, less when stable)
 *   - Automatic reconnection detection and retry logic
 *   - Event-driven architecture for easy integration
 *   - Offline data backup/restore capabilities
 *   - Configurable via Unity Inspector
 *   - DontDestroyOnLoad persistence across scenes
 * 
 * Usage Example:
 *   ```csharp
 *   // Subscribe to connectivity changes
 *   OfflineNetworkManager.Instance.OnInternetConnectivityChanged += (isOnline) => {
 *       Debug.Log($"Internet is now: {(isOnline ? "Online" : "Offline")}");
 *   };
 *   
 *   // Check if you can make a network request
 *   if (OfflineNetworkManager.Instance.CanAttemptSync()) {
 *       // Make your network request here
 *   }
 *   
 *   // Mark sync status
 *   OfflineNetworkManager.Instance.MarkSyncSucceeded();
 *   ```
 * 
 * Adaptive Intervals (Default):
 *   First minute:    5s   - Quick detection of state changes
 *   1-10 minutes:    10s  - Moderate monitoring
 *   10-60 minutes:   30s  - Reduced frequency
 *   1-10 hours:      600s - Low frequency monitoring
 *   10+ hours:       3600s- Minimal checks (battery saving)
 * 
 * GitHub: https://github.com/DryreL/UnityOfflineNetworkManager
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Newtonsoft.Json;

/// <summary>
/// Singleton manager for internet connectivity detection and offline handling.
/// Automatically adjusts check frequency based on connection duration for optimal performance.
/// </summary>
public class OfflineNetworkManager : MonoBehaviour
{
    // ========================================================================
    // SINGLETON PATTERN
    // ========================================================================
    // Thread-safe singleton instance for global access
    private static OfflineNetworkManager instance;
    
    /// <summary>
    /// Gets the singleton instance of OfflineNetworkManager.
    /// Automatically creates an instance if one doesn't exist.
    /// </summary>
    public static OfflineNetworkManager Instance
    {
        get
        {
            if (instance == null)
            {
                // Try to find existing instance in scene
                instance = FindFirstObjectByType<OfflineNetworkManager>();
                
                // Create new instance if none exists
                if (instance == null)
                {
                    GameObject obj = new GameObject("OfflineNetworkManager");
                    instance = obj.AddComponent<OfflineNetworkManager>();
                }
            }
            return instance;
        }
    }

    // ========================================================================
    // NETWORK STATUS ENUM
    // ========================================================================
    
    /// <summary>
    /// Represents the current network connectivity state and sync status.
    /// </summary>
    public enum NetworkStatus
    {
        /// <summary>Connected to internet and ready for network operations</summary>
        Online = 0,
        
        /// <summary>Offline with data waiting to be synced when connection is restored</summary>
        OfflinePending = 1,
        
        /// <summary>Offline with no pending sync data</summary>
        OfflineNoData = 2
    }

    // ========================================================================
    // CONFIGURATION PROPERTIES (Editable in Unity Inspector)
    // ========================================================================
    
    [Tooltip("Enable automatic internet connectivity detection and monitoring")]
    public bool enableOfflineDetection = true;

    [Header("Adaptive Check Intervals (Seconds)")]
    [Tooltip("Check interval during first minute (default: 5s) - Frequent checks for quick state change detection")]
    public float checkIntervalFirst1Minute = 5f;
    
    [Tooltip("Check interval for 1-10 minutes (default: 10s) - Moderate monitoring frequency")]
    public float checkInterval1To10Minutes = 10f;
    
    [Tooltip("Check interval for 10-60 minutes (default: 30s) - Reduced frequency for stable connections")]
    public float checkInterval10To60Minutes = 30f;
    
    [Tooltip("Check interval for 1-10 hours (default: 600s/10min) - Low frequency monitoring")]
    public float checkInterval1To10Hours = 600f;
    
    [Tooltip("Check interval after 10+ hours (default: 3600s/1hr) - Minimal checks for battery conservation")]
    public float checkInterval10HoursPlus = 3600f;

    [Header("Retry & Logging Settings")]
    [Tooltip("Minimum delay before retrying a failed sync operation (default: 60s)")]
    public float syncRetryDebounceSeconds = 60f;
	
    [Tooltip("Display warning messages when network issues are detected")]
    public bool showNetworkWarnings = true;

    [Tooltip("Enable detailed debug logging for troubleshooting")]
    public bool enableDebugLogs = false;

    // ========================================================================
    // STATE VARIABLES (Private Runtime State)
    // ========================================================================
    
    // Connectivity state tracking
    private bool isOnline = true;                    // Current online/offline status
    private bool hasPendingSyncData = false;         // Whether there's data waiting to sync
    private float lastInternetCheckTime = 0f;        // Timestamp of last connectivity check
    private float lastFailedSyncTime = 0f;           // Timestamp of last failed sync attempt
    private bool isInitialized = false;              // Whether Initialize() has been called
    
    // Duration tracking for adaptive intervals
    private float offlineStartTime = 0f;             // When connection was lost (for calculating offline duration)
    private float onlineStartTime = 0f;              // When connection was restored (for calculating online duration)

    // ========================================================================
    // EVENTS (Subscribe to these for connectivity notifications)
    // ========================================================================
    
    /// <summary>
    /// Fired when a failed sync is ready to be retried (after debounce period).
    /// </summary>
    public event Action OnSyncRetryReady;
    
    /// <summary>
    /// Fired when the network status changes (Online/OfflinePending/OfflineNoData).
    /// </summary>
    public event Action<NetworkStatus> OnNetworkStatusChanged;
    
    /// <summary>
    /// Fired when internet connectivity changes. Parameter: true = online, false = offline.
    /// </summary>
    public event Action<bool> OnInternetConnectivityChanged;

    // ========================================================================
    // UI HELPER EVENTS (Inspector-Callable for UI Integration)
    // ========================================================================
    // Use these UnityEvents to connect UI panels and UI elements in the Inspector
    // without writing additional code. See OnConnectionLostUI(), OnConnectionRestoredUI(), etc.
    
    /// <summary>
    /// Unity Event called when internet connection is lost.
    /// Use this to show offline UI, disable buttons, etc.
    /// Inspector: Drag your UI Panel or Button here
    /// </summary>
	[Header("Events")]
    [SerializeField]
    private UnityEvent OnConnectionLostEvent = new UnityEvent();
    
    /// <summary>
    /// Unity Event called when internet connection is restored and stable.
    /// Use this to hide offline UI, enable buttons, show sync status, etc.
    /// Inspector: Drag your UI Panel or Button here
    /// </summary>
    [SerializeField]
    private UnityEvent OnConnectionRestoredEvent = new UnityEvent();
    
    /// <summary>
    /// Unity Event called when attempting to reconnect after being offline.
    /// Use this to show "Connecting..." UI, spinning indicator, etc.
    /// </summary>
    [SerializeField]
    private UnityEvent OnReconnectingEvent = new UnityEvent();
    
    /// <summary>
    /// Unity Event called when sync data is ready to be processed.
    /// Use this to show progress bar, "Syncing..." message, etc.
    /// </summary>
    [SerializeField]
    private UnityEvent OnSyncRequiredEvent = new UnityEvent();

    // ========================================================================
    // LIFECYCLE
    // ========================================================================
    
    /// <summary>
    /// Ensure OfflineNetworkManager initializes before all other scripts
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureInitialized()
    {
        // This will trigger the Instance getter, which creates the manager if needed
        var manager = Instance;
        if (manager != null && manager.enableDebugLogs)
        {
            Debug.Log("[OfflineNetworkManager] ✅ Pre-initialized before scene load");
        }
    }
    
    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Initialize time tracking (assume online at start)
            onlineStartTime = Time.time;
            
            if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] ? Initialized");
        }
        else if (instance != this)
        {
            Debug.LogWarning("[OfflineNetworkManager] ?? Duplicate instance, destroying");
            Destroy(gameObject);
        }
    }
    
    void OnDestroy()
    {
        // Only cleanup during actual gameplay, not during editor mode changes
        if (!Application.isPlaying)
            return;
            
        try
        {
            if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] ? OnDestroy called");
            
            // Clear all event subscribers to prevent memory leaks
            if (OnSyncRetryReady != null)
            {
                foreach (System.Delegate d in OnSyncRetryReady.GetInvocationList())
                    OnSyncRetryReady -= (System.Action)d;
            }
            if (OnNetworkStatusChanged != null)
            {
                foreach (System.Delegate d in OnNetworkStatusChanged.GetInvocationList())
                    OnNetworkStatusChanged -= (System.Action<NetworkStatus>)d;
            }
            if (OnInternetConnectivityChanged != null)
            {
                foreach (System.Delegate d in OnInternetConnectivityChanged.GetInvocationList())
                    OnInternetConnectivityChanged -= (System.Action<bool>)d;
            }
            
            // Clear instance if this is the active instance
            if (instance == this)
            {
                instance = null;
            }
            
            if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] ? Cleanup completed");
        }
        catch (System.Exception ex)
        {
            // Suppress errors during scene cleanup
            Debug.LogWarning($"[OfflineNetworkManager] Exception during OnDestroy: {ex.Message}");
        }
    }

    /// <summary>
    /// Unity Update loop - Handles periodic connectivity checks and retry logic.
    /// Uses adaptive intervals that adjust based on connection duration.
    /// </summary>
    void Update()
    {
        // ====================================================================
        // RETRY LOGIC: Trigger retry events when debounce period has elapsed
        // ====================================================================
        if (hasPendingSyncData && isOnline)
        {
            float timeSinceLastFail = Time.time - lastFailedSyncTime;
            if (timeSinceLastFail >= syncRetryDebounceSeconds)
            {
                if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] ?? Retry ready");
                OnSyncRetryReady?.Invoke();
                OnSyncRequiredEvent?.Invoke();
                hasPendingSyncData = false;
            }
        }
        
        // ====================================================================
        // ADAPTIVE CONNECTIVITY CHECKS: Adjust frequency based on duration
        // ====================================================================
        if (enableOfflineDetection)
        {
            // Calculate how long we've been in current state (online or offline)
            float stateDuration = isOnline ? (Time.time - onlineStartTime) : (Time.time - offlineStartTime);
            float checkInterval;
            
            // Determine check interval based on connection duration
            // More frequent when state recently changed, less frequent when stable
            if (stateDuration < 60f)
            {
                // First minute: Frequent checks (5s) for quick state change detection
                checkInterval = checkIntervalFirst1Minute;
            }
            else if (stateDuration < 600f)
            {
                // 1-10 minutes: Moderate frequency (10s)
                checkInterval = checkInterval1To10Minutes;
            }
            else if (stateDuration < 3600f)
            {
                // 10-60 minutes: Reduced frequency (30s) for stable connections
                checkInterval = checkInterval10To60Minutes;
            }
            else if (stateDuration < 36000f)
            {
                // 1-10 hours: Low frequency (10min) monitoring
                checkInterval = checkInterval1To10Hours;
            }
            else
            {
                // 10+ hours: Minimal checks (1hr) for battery/resource conservation
                checkInterval = checkInterval10HoursPlus;
            }
            
            // Perform connectivity check if interval has elapsed
            if (Time.time - lastInternetCheckTime >= checkInterval)
            {
                lastInternetCheckTime = Time.time;
                CheckInternetConnection();
                
                if (enableDebugLogs)
                {
                    string state = isOnline ? "Online" : "Offline";
                    Debug.Log($"[OfflineNetworkManager] {state} for {stateDuration:F0}s - checking with interval {checkInterval}s");
                }
            }
        }
    }

    // ========================================================================
    // PUBLIC API
    // ========================================================================

    /// <summary>
    /// Initializes the OfflineNetworkManager with custom configuration.
    /// Note: Only the first call to Initialize() will take effect (idempotent).
    /// Adaptive check intervals are configured via Inspector and not affected by this method.
    /// </summary>
    /// <param name="enableDetection">Enable automatic connectivity detection</param>
    /// <param name="checkInterval">Legacy parameter - ignored (use Inspector intervals instead)</param>
    /// <param name="retryDebounce">Minimum seconds between retry attempts</param>
    /// <param name="debugLogs">Enable detailed debug logging</param>
    /// <param name="warnings">Show warning messages for network issues</param>
    public void Initialize(bool enableDetection, float checkInterval, float retryDebounce, bool debugLogs, bool warnings)
    {
        if (isInitialized) return; // Skip if already initialized
        isInitialized = true;
        
        enableOfflineDetection = enableDetection;
        syncRetryDebounceSeconds = retryDebounce;
        enableDebugLogs = debugLogs;
        showNetworkWarnings = warnings;

        if (enableDebugLogs)
        {
            Debug.Log($"[OfflineNetworkManager] Configured: retryDebounce={syncRetryDebounceSeconds}s (using adaptive intervals)");
        }

        if (enableOfflineDetection)
        {
            CheckInternetConnection();
        }
    }

    /// <summary>
    /// Checks if the device currently has internet connectivity.
    /// </summary>
    /// <returns>True if online, false if offline</returns>
    public bool IsOnline()
    {
        return isOnline;
    }

    /// <summary>
    /// Checks if there is data waiting to be synced when connection is restored.
    /// </summary>
    /// <returns>True if there's pending sync data, false otherwise</returns>
    public bool HasPendingSyncData()
    {
        return hasPendingSyncData;
    }

    /// <summary>
    /// Gets the current network status including sync state.
    /// </summary>
    /// <returns>NetworkStatus enum value (Online/OfflinePending/OfflineNoData)</returns>
    public NetworkStatus GetNetworkStatus()
    {
        if (isOnline)
            return NetworkStatus.Online;
        
        if (hasPendingSyncData)
            return NetworkStatus.OfflinePending;
        
        return NetworkStatus.OfflineNoData;
    }

    /// <summary>
    /// Gets the time remaining until the next retry attempt for failed syncs.
    /// </summary>
    /// <returns>Seconds remaining until retry, or 0 if no retry pending</returns>
    public float GetRetryCountdown()
    {
        if (!hasPendingSyncData || isOnline)
            return 0f;
        
        float timeSinceLastFail = Time.time - lastFailedSyncTime;
        float remaining = syncRetryDebounceSeconds - timeSinceLastFail;
        return Mathf.Max(0f, remaining);
    }

    /// <summary>
    /// Updates the retry debounce duration at runtime.
    /// </summary>
    /// <param name="seconds">New debounce duration in seconds (minimum 5s)</param>
    public void SetSyncRetryDebounce(float seconds)
    {
        syncRetryDebounceSeconds = Mathf.Max(5f, seconds);
        if (enableDebugLogs)
        {
            Debug.Log($"[OfflineNetworkManager] Retry debounce set to {syncRetryDebounceSeconds}s");
        }
    }

    /// <summary>
    /// Forces an immediate sync retry if online, bypassing the debounce timer.
    /// Useful for user-initiated retry actions.
    /// </summary>
    public void ForceSyncIfOnline()
    {
        if (isOnline && hasPendingSyncData)
        {
            if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] ?? Force sync");
            hasPendingSyncData = false;
            OnSyncRetryReady?.Invoke();
        }
    }

    /// <summary>
    /// Marks a sync operation as failed. Sets offline state and schedules retry.
    /// Call this when your network request fails.
    /// </summary>
    public void MarkSyncFailed()
    {
        isOnline = false;
        hasPendingSyncData = true;
        lastFailedSyncTime = Time.time;
        
        if (showNetworkWarnings)
        {
            Debug.LogWarning("[OfflineNetworkManager] ?? Sync failed - will retry");
        }
    }

    /// <summary>
    /// Marks a sync operation as successful. Clears pending sync flag.
    /// Call this when your network request succeeds.
    /// </summary>
    public void MarkSyncSucceeded()
    {
        hasPendingSyncData = false;
        isOnline = true;
        
        if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] ? Sync succeeded");
    }

    /// <summary>
    /// Checks if a sync attempt can be made (guards against offline spam).
    /// Always call this before making network requests.
    /// </summary>
    /// <returns>True if online and ready to sync, false if offline</returns>
    public bool CanAttemptSync()
    {
        if (!isOnline)
        {
            if (showNetworkWarnings)
            {
                Debug.LogWarning("[OfflineNetworkManager] ?? Cannot sync - offline");
            }
            hasPendingSyncData = true;
            lastFailedSyncTime = Time.time;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Saves data to PlayerPrefs for offline persistence.
    /// Only saves when offline to avoid unnecessary disk writes.
    /// </summary>
    /// <param name="data">Dictionary of data to backup</param>
    /// <param name="backupKey">PlayerPrefs key for the backup data</param>
    /// <param name="userIdKey">PlayerPrefs key for the user ID</param>
    /// <param name="userId">Current user ID (to validate backup ownership)</param>
    public void SaveOfflineDataBackup(Dictionary<string, int> data, string backupKey, string userIdKey, string userId)
    {
        if (!isOnline && data != null && data.Count > 0)
        {
            try
            {
                string json = JsonConvert.SerializeObject(data);
                PlayerPrefs.SetString(backupKey, json);
                PlayerPrefs.SetString(userIdKey, userId);
                PlayerPrefs.Save();
                
                if (enableDebugLogs) Debug.Log($"[OfflineNetworkManager] ?? Backed up {data.Count} items");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfflineNetworkManager] Backup failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Restores previously saved offline data from PlayerPrefs.
    /// Validates that the backup belongs to the current user before restoring.
    /// Automatically cleans up backup data after successful restore.
    /// </summary>
    /// <param name="backupKey">PlayerPrefs key for the backup data</param>
    /// <param name="userIdKey">PlayerPrefs key for the user ID</param>
    /// <param name="userId">Current user ID (must match backup owner)</param>
    /// <returns>Restored data dictionary, or null if no valid backup exists</returns>
    public Dictionary<string, int> RestoreOfflineDataBackup(string backupKey, string userIdKey, string userId)
    {
        try
        {
            if (!PlayerPrefs.HasKey(backupKey))
                return null;

            // Validate backup ownership
            string backupUserId = PlayerPrefs.GetString(userIdKey, "");
            if (backupUserId != userId)
            {
                if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] Backup from different user - ignoring");
                return null;
            }

            // Deserialize backup data
            string json = PlayerPrefs.GetString(backupKey, "");
            if (string.IsNullOrEmpty(json))
                return null;

            var backup = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
            
            // Clean up after successful restore
            PlayerPrefs.DeleteKey(backupKey);
            PlayerPrefs.DeleteKey(userIdKey);
            PlayerPrefs.Save();

            if (backup != null && backup.Count > 0)
            {
                hasPendingSyncData = true;
                if (enableDebugLogs) Debug.Log($"[OfflineNetworkManager] ?? Restored {backup.Count} items");
                return backup;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[OfflineNetworkManager] Restore failed: {ex.Message}");
        }

        return null;
    }

    // ========================================================================
    // PRIVATE METHODS (Internal Implementation)
    // ========================================================================

    /// <summary>
    /// Checks current internet connectivity using Unity's Application.internetReachability.
    /// Tracks state changes and fires appropriate events when connectivity changes.
    /// Updates online/offline timestamps for adaptive interval calculation.
    /// </summary>
    private void CheckInternetConnection()
    {
        bool wasOnline = isOnline;
        NetworkStatus previousStatus = GetNetworkStatus();

        // Check Unity's built-in reachability status
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            isOnline = false;
            
            // Track offline start time for adaptive intervals
            if (wasOnline)
            {
                offlineStartTime = Time.time;
                if (showNetworkWarnings)
                {
                    Debug.LogWarning("[OfflineNetworkManager] ?? NO INTERNET");
                }
            }
        }
        else if (Application.internetReachability == NetworkReachability.ReachableViaCarrierDataNetwork)
        {
            isOnline = true;
            
            // Track online start time for adaptive intervals
            if (!wasOnline)
            {
                onlineStartTime = Time.time;
                if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] ?? Online (Mobile Data)");
            }
        }
        else if (Application.internetReachability == NetworkReachability.ReachableViaLocalAreaNetwork)
        {
            isOnline = true;
            
            // Track online start time for adaptive intervals
            if (!wasOnline)
            {
                onlineStartTime = Time.time;
                if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] ?? Online (WiFi/LAN)");
            }
        }

        // Log connection loss
        if (wasOnline && !isOnline && enableDebugLogs)
        {
            Debug.LogWarning("[OfflineNetworkManager] ?? Connection lost");
        }
        
        // Fire connectivity change event if state changed
        if (wasOnline != isOnline)
        {
            OnInternetConnectivityChanged?.Invoke(isOnline);
            
            // Fire UI helper events
            if (isOnline)
            {
                // Connection restored
                OnConnectionRestoredEvent?.Invoke();
                if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] OnConnectionRestoredEvent fired");
            }
            else
            {
                // Connection lost
                OnConnectionLostEvent?.Invoke();
                if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] OnConnectionLostEvent fired");
            }
        }
        
        // Fire network status change event if status changed
        NetworkStatus newStatus = GetNetworkStatus();
        if (newStatus != previousStatus)
        {
            OnNetworkStatusChanged?.Invoke(newStatus);
        }
    }
    
    /// <summary>
    /// Manually triggers an immediate internet connectivity check.
    /// Bypasses the adaptive interval timer.
    /// </summary>
    public void CheckInternetConnectionNow()
    {
        CheckInternetConnection();
    }
    
    /// <summary>
    /// Determines if a failed network request should be retried based on the error type.
    /// </summary>
    /// <param name="responseCode">HTTP response code (e.g., 404, 500)</param>
    /// <param name="errorType">Error type string (e.g., "ConnectionError", "Timeout")</param>
    /// <returns>True if the request should be retried, false otherwise</returns>
    public bool ShouldRetryRequest(long responseCode, string errorType)
    {
        // Don't retry client authentication/authorization errors
        if (responseCode == 401 || responseCode == 403)
            return false;
        
        // Don't retry "not found" errors
        if (responseCode == 404)
            return false;
        
        // Don't retry bad request errors (client-side issue)
        if (responseCode == 400)
            return false;
        
        // Retry on server errors (5xx) and connection errors
        if (responseCode >= 500 || errorType == "ConnectionError")
            return true;
        
        // Retry on timeout
        if (errorType == "Timeout")
            return true;
        
        return false;
    }
    
    // ========================================================================
    // UI HELPER METHODS (For Manual UI Control or Testing)
    // ========================================================================
    
    /// <summary>
    /// Manually trigger the "Connection Lost" UI event.
    /// Useful for testing UI without actually losing internet.
    /// Can be called from buttons, scripts, or animations.
    /// </summary>
    public void OnConnectionLostUI()
    {
        if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] Manual OnConnectionLostUI triggered");
        OnConnectionLostEvent?.Invoke();
    }
    
    /// <summary>
    /// Manually trigger the "Connection Restored" UI event.
    /// Useful for testing UI without actually restoring internet.
    /// Can be called from buttons, scripts, or animations.
    /// </summary>
    public void OnConnectionRestoredUI()
    {
        if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] Manual OnConnectionRestoredUI triggered");
        OnConnectionRestoredEvent?.Invoke();
    }
    
    /// <summary>
    /// Manually trigger the "Reconnecting" UI event.
    /// Shows "Attempting to reconnect..." message or indicator.
    /// Can be called from buttons, scripts, or animations.
    /// </summary>
    public void OnReconnectingUI()
    {
        if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] Manual OnReconnectingUI triggered");
        OnReconnectingEvent?.Invoke();
    }
    
    /// <summary>
    /// Manually trigger the "Sync Required" UI event.
    /// Shows progress bar, "Syncing..." message, or status indicator.
    /// Can be called from buttons, scripts, or animations.
    /// </summary>
    public void OnSyncRequiredUI()
    {
        if (enableDebugLogs) Debug.Log("[OfflineNetworkManager] Manual OnSyncRequiredUI triggered");
        OnSyncRequiredEvent?.Invoke();
    }
}
