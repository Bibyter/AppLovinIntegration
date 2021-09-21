using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Client
{
    [DefaultExecutionOrder(0)]
    public sealed class AppLovin : MonoBehaviour
    {
        [SerializeField] string _sdkKey;

        #region
        [System.Serializable]
        public struct Keys
        {
            public string rvKey;
            public string interKey;
            public string bannerKey;
        }

        [SerializeField, Space] Keys _androidKeys;
        [SerializeField] Keys _iosKeys;

        Keys platformKeys
        {
            get
            {
#if UNITY_ANDROID
                return _androidKeys;
#else
                return _iosKeys;
#endif
            }
        }

        string rvKey => platformKeys.rvKey;
        string interKey => platformKeys.interKey;
        #endregion

        [SerializeField, Space] float _interCooldownDuration = 30f;

        public bool skipInterstital { private set; get; }
        public bool needShowGdrp { private set; get; }

        private void Start()
        {
            Debug.Log("AppLovin.Start");

            MaxSdkCallbacks.OnSdkInitializedEvent += (MaxSdkBase.SdkConfiguration sdkConfiguration) =>
            {
                // AppLovin SDK is initialized, start loading ads
                InitInterstitials();
                InitRewardedVideos();
                InitializeBannerAds();
#if DEBUG
                MaxSdk.ShowMediationDebugger();
#endif

                if (sdkConfiguration.ConsentDialogState == MaxSdkBase.ConsentDialogState.Applies)
                {
                    needShowGdrp = true;

#if UNITY_IOS || UNITY_IPHONE || UNITY_EDITOR
                    // не показываем внутриигровой gdrp для ios больше 14.5 верисии
                    if (MaxSdkUtils.CompareVersions(UnityEngine.iOS.Device.systemVersion, "14.5") != MaxSdkUtils.VersionComparisonResult.Lesser)
                    {
                        needShowGdrp = false;
                    }
#endif
                }
                else if (sdkConfiguration.ConsentDialogState == MaxSdkBase.ConsentDialogState.DoesNotApply)
                {
                    // No need to show consent dialog, proceed with initialization
                }
                else
                {
                    // Consent dialog state is unknown. Proceed with initialization, but check if the consent
                    // dialog should be shown on the next application initialization
                }

                Debug.Log("AppLovin.OnInitializedEvent");
            };

            MaxSdk.SetSdkKey(_sdkKey);
            MaxSdk.InitializeSdk();

            StartCoroutine(WaitInternetConnection(
                () => {
                    if (!MaxSdk.IsInitialized())
                        MaxSdk.InitializeSdk();
                }
            ));
        }

        IEnumerator WaitInternetConnection(System.Action action)
        {
            while (true)
            {
                var request = new UnityEngine.Networking.UnityWebRequest("http://google.com");
                yield return request.SendWebRequest();

                if (request.error == null)
                {
                    action?.Invoke();
                    yield break;
                }

                yield return new WaitForSeconds(10f);
            }
        }

        #region Inter
        public delegate void InterCallback(bool isShow);
        InterCallback _interCallback;

        System.Object _interData;

        int _interstitialLoadingRetries;
        double _prevShowAdsTime;

        void InitInterstitials()
        {
            // Attach callbacks
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnInterstitialLoadedEvent;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnInterstitialLoadFailedEvent;
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += OnInterstitialDisplayedEvent;
            MaxSdkCallbacks.Interstitial.OnAdClickedEvent += OnInterstitialClickedEvent;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnInterstitialDismissedEvent;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialAdFailedToDisplayEvent;

            // Load the first interstitial
            LoadInterstitial();
        }

        void LoadInterstitial()
        {
            MaxSdk.LoadInterstitial(interKey);
            Debug.Log($"AppLovin.LoadInterstitial(adUnitId={interKey})");
        }

        public void ShowInterstitial(InterCallback callback, System.Object data)
        {
            double delta = Time.time - _prevShowAdsTime;


            if (skipInterstital || delta < _interCooldownDuration || !MaxSdk.IsInterstitialReady(interKey))
            {
                callback?.Invoke(false);
                return;
            }

            MaxSdk.ShowInterstitial(interKey);
            _interCallback = callback;
            _interData = data;

            
        }

        private void OnInterstitialLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log($"AppLovin.OnInterstitialLoadedEvent(adUnitId={adUnitId}, adInfo={adInfo})");
            // Interstitial ad is ready for you to show. MaxSdk.IsInterstitialReady(adUnitId) now returns 'true'

            // Reset retry attempt
            _interstitialLoadingRetries = 0;
        }

        private void OnInterstitialLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            Debug.Log($"AppLovin.OnInterstitialLoadFailedEvent(adUnitId={adUnitId}, errorInfo={errorInfo})");
            // Interstitial ad failed to load
            // AppLovin recommends that you retry with exponentially higher delays, up to a maximum delay (in this case 64 seconds)

            _interstitialLoadingRetries++;
            double retryDelay = Mathf.Pow(2, Mathf.Min(6, _interstitialLoadingRetries));

            Invoke(nameof(LoadInterstitial), (float)retryDelay);

        }

        private void OnInterstitialDisplayedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("AppLovin.OnInterstitialDisplayedEvent");
            interDisplayed?.Invoke(_interData);
        }

        private void OnInterstitialAdFailedToDisplayEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log($"AppLovin.OnInterstitialAdFailedToDisplayEvent(adUnitId={adUnitId}, errorInfo={errorInfo}, adInfo={adInfo})");
            // Interstitial ad failed to display. AppLovin recommends that you load the next ad.
            LoadInterstitial();

            _interCallback?.Invoke(false);
            _interCallback = null;
            _interData = null;
        }

        private void OnInterstitialClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("AppLovin.OnInterstitialClickedEvent");
            //TrackVideoAdsWatch("interstitial","inter_between_levels","clicked");
        }

        private void OnInterstitialDismissedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("AppLovin.OnInterstitialDismissedEvent");

            _prevShowAdsTime = Time.time;
            // Interstitial ad is hidden. Pre-load the next ad.
            LoadInterstitial();

            _interCallback?.Invoke(false);
            _interCallback = null;

            interHide?.Invoke(_interData);
            _interData = null;

        }
        #endregion

        #region rewarded videos

        public delegate void RvCallback(bool isShowed, bool isEarnedReward);
        RvCallback _rvCallback;

        System.Object _rvData;

        bool _isRvShowed;
        bool _isRvReceiveReward;
        int _rewardedVideoLoadingRetries;

        void InitRewardedVideos()
        {
            _isRvShowed = false;
            _isRvReceiveReward = false;

            // Attach callback
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnRewardedAdLoadedEvent;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnRewardedAdLoadFailedEvent;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += OnRewardedAdDisplayedEvent;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnRewardedAdFailedToDisplayEvent;
            MaxSdkCallbacks.Rewarded.OnAdClickedEvent += OnRewardedAdClickedEvent;
            //MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnRewardedAdRevenuePaidEvent;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnRewardedAdHiddenEvent;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += OnRewardedAdReceivedRewardEvent;

            // Load the first rewarded ad
            LoadRewardedVideo();
        }
        void RvResetShowPrms()
        {
            _isRvShowed = false;
            _rvCallback?.Invoke(false, false);
            _rvCallback = null;
            _rvData = null;
        }

        void LoadRewardedVideo()
        {
            MaxSdk.LoadRewardedAd(rvKey);
        }

        public bool IsRewardedVideoAvailable()
        {
            return MaxSdk.IsInitialized() && MaxSdk.IsRewardedAdReady(rvKey);
        }

        


        public void ShowRewardedVideo(string placement, RvCallback callback, System.Object data = null)
        {
            Debug.Log($"AppLovin.ShowRewardedVideo(placement={placement})");

            if (!IsRewardedVideoAvailable())
            {
                callback?.Invoke(false, false);
                return;
            }

            MaxSdk.ShowRewardedAd(rvKey);
            _rvCallback = callback;
            _isRvShowed = true;
            _isRvReceiveReward = false;
            _rvData = data;

            
        }

        private void OnRewardedAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("AppLovin.OnRewardedAdLoadedEvent");
            // Rewarded ad is ready for you to show. MaxSdk.IsRewardedAdReady(adUnitId) now returns 'true'.

            // Reset retry attempt
            _rewardedVideoLoadingRetries = 0;
        }

        private void OnRewardedAdLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            Debug.Log($"AppLovin.OnRewardedAdLoadFailedEvent(errorInfo={errorInfo})");
            // Rewarded ad failed to load
            // AppLovin recommends that you retry with exponentially higher delays, up to a maximum delay (in this case 64 seconds).

            _rewardedVideoLoadingRetries++;
            double retryDelay = Mathf.Pow(2, Mathf.Min(6, _rewardedVideoLoadingRetries));

            Invoke(nameof(LoadRewardedVideo), (float)retryDelay);
        }

        private void OnRewardedAdDisplayedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("AppLovin.OnRewardedAdDisplayedEvent");
            rvDisplayed?.Invoke(_rvData);
        }

        private void OnRewardedAdFailedToDisplayEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
        {
            // Rewarded ad failed to display. AppLovin recommends that you load the next ad.
            LoadRewardedVideo();

            RvResetShowPrms();
        }

        private void OnRewardedAdClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("AppLovin.OnRewardedAdClickedEvent");
        }

        private void OnRewardedAdHiddenEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("AppLovin.OnRewardedAdHiddenEvent");

            _prevShowAdsTime = Time.time;

            RvResetShowPrms();
            // Rewarded ad is hidden. Pre-load the next ad
            LoadRewardedVideo();
        }

        private void OnRewardedAdReceivedRewardEvent(string adUnitId, MaxSdk.Reward reward, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log($"AppLovin.OnRewardedAdReceivedRewardEvent(reward={reward}, adInfo={adInfo})");

            _isRvReceiveReward = true;
            rvReceivedReward?.Invoke(_rvData);
        }

        #endregion

        //----------------------------------------------------------------------
        // events tracking
        //----------------------------------------------------------------------

        public void SetHasUserConsent(bool hasUserConsent)
        {
            MaxSdk.SetHasUserConsent(hasUserConsent);
        }

        #region Banner
        [SerializeField, Space] bool _bannerIsActive = true;
        string _bannerAdUnitId;
        bool _bannerIsShowed;

        void InitializeBannerAds()
        {
            _bannerAdUnitId = platformKeys.bannerKey;
            _bannerIsShowed = false;

            // Banners are automatically sized to 320×50 on phones and 728×90 on tablets
            // You may call the utility method MaxSdkUtils.isTablet() to help with view sizing adjustments

            if (!_bannerIsActive) return;

            MaxSdk.CreateBanner(_bannerAdUnitId, MaxSdkBase.BannerPosition.BottomCenter);

            MaxSdk.SetBannerExtraParameter(_bannerAdUnitId, "adaptive_banner", "false");

            // Set background or background color for banners to be fully functional
            MaxSdk.SetBannerBackgroundColor(_bannerAdUnitId, Color.black);
        }

        public void ShowBanner()
        {
            if (!_bannerIsActive) return;
            if (_bannerIsShowed || !MaxSdk.IsInitialized()) return;
            MaxSdk.ShowBanner(_bannerAdUnitId);
            _bannerIsShowed = true;
        }

        public void HideBanner()
        {
            if (!_bannerIsActive) return;
            if (!_bannerIsShowed) return;
            MaxSdk.HideBanner(_bannerAdUnitId);
            _bannerIsShowed = false;
        }

        public bool isBannerShowed => _bannerIsShowed;
        public bool isBannerActive => _bannerIsActive;
#endregion

#region
        public static event System.Action<System.Object> rvDisplayed;
        public static event System.Action<System.Object> rvReceivedReward;
        public static event System.Action<System.Object> interDisplayed;
        public static event System.Action<System.Object> interHide;
#endregion
    }
}
