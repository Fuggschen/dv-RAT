using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// SerializeField fields are assigned by Unity Inspector; some fields reserved for future use.
#pragma warning disable 0169, 0414, 0649

namespace DVRAT
{
    /// <summary>
    /// The tablet's HDD sounds. Clips set in the prefab win. Empty ones load from the mod's <c>sounds</c> folder
    /// as hdd_startup, hdd_hum, hdd_click, hdd_busy and shutdown (.wav).
    /// </summary>
    public class RATAudioManager : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Audio Sources")]
        [SerializeField] private AudioSource _mainSource;
        [SerializeField] private AudioSource _loopSource;
        [SerializeField] private AudioSource _busySource;

        [Header("Audio Clips")]
        [SerializeField] private AudioClip _hddStartupClip;
        [SerializeField] private AudioClip _hddHumClip;
        [SerializeField] private AudioClip _hddClickClip;
        [SerializeField] private AudioClip _hddBusyClip;
        [SerializeField] private AudioClip _shutdownClip;

        [Header("Volume Settings")]
        [SerializeField] [Range(0f, 1f)] private float _masterVolume = 0.7f;
        [SerializeField] [Range(0f, 1f)] private float _humVolume = 0.3f;
        [SerializeField] [Range(0f, 1f)] private float _clickVolume = 0.5f;
        [SerializeField] [Range(0f, 1f)] private float _startupVolume = 0.8f;

        [Header("3D Audio")]
        [SerializeField] [Range(0f, 1f)] private float _spatialBlend = 1.0f;

        #endregion

        #region Private Fields

        public const string SoundsFolder = "sounds";

        /// <summary>A startup requested while its clip loads still plays if loading finishes within this time.</summary>
        private const float LateStartupWindow = 3f;

        /// <summary>The startup clip fades over its last half second, and the hum starts there.</summary>
        private const float StartupOverlap = 0.5f;

        /// <summary>The hum settles from startup loudness to its own volume over this time.</summary>
        private const float HumSettleSeconds = 3f;

        private bool _hddActive = false;
        private float _startupRequestedAt = float.NegativeInfinity;
        private bool _startupPending;
        private double _humStartsAt = double.NegativeInfinity;
        private Coroutine _humSettle;

        // Seek ticks get their own source so their random pitch doesn't bend other sounds.
        private AudioSource _tickSource;
        private float _nextTickAt;
        private bool _seeking = true;
        // Until then the disk rattles in bursts instead of seeking now and then.
        private float _readingUntil = float.NegativeInfinity;
        private int _burstLeft;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_mainSource == null) _mainSource = NewSource("RAT sounds");
            if (_loopSource == null) _loopSource = NewSource("RAT disk hum");
            if (_busySource == null) _busySource = NewSource("RAT disk busy");

            _tickSource = NewSource("RAT disk seek");

            ConfigureSource(_mainSource, false);
            ConfigureSource(_loopSource, true);
            ConfigureSource(_busySource, true);
            ConfigureSource(_tickSource, false);
        }

        private void Update()
        {
            if (!_seeking || !_hddActive || _hddClickClip == null || _loopSource == null || !_loopSource.isPlaying) return;
            if (Time.time < _nextTickAt) return;

            if (Time.time < _readingUntil)
            {
                if (_burstLeft <= 0) _burstLeft = Random.Range(3, 9);
                PlayTick(Random.Range(0.4f, 0.75f));
                _burstLeft--;
                _nextTickAt = Time.time + (_burstLeft > 0 ? Random.Range(0.025f, 0.06f) : Random.Range(0.07f, 0.2f));
                return;
            }

            PlayTick(Random.Range(0.25f, 0.5f));
            _nextTickAt = Time.time + (Random.value < 0.4f ? Random.Range(0.06f, 0.18f) : Random.Range(0.4f, 2.5f));
        }

        private void Start()
        {
            // No StopAll here: the controller may already have started the boot sound in its own Start.
            StartCoroutine(LoadMissingClips());
        }

        #endregion

        #region Public API

        public void PlayHDDStartup()
        {
            _seeking = true;
            if (_hddStartupClip == null)
            {
                _startupPending = true;
                _startupRequestedAt = Time.time;
                return;
            }
            if (_mainSource == null) return;

            _mainSource.PlayOneShot(_hddStartupClip, _startupVolume * _masterVolume);
            // Scheduled on the audio clock so there is no gap.
            if (_hddHumClip != null && _loopSource != null)
                ScheduleHum(AudioSettings.dspTime + Mathf.Max(0f, _hddStartupClip.length - StartupOverlap), afterStartup: true);
            RATMain.Log("HDD startup sound played.");
        }

        /// <summary>Does nothing if the startup sound already scheduled the hum.</summary>
        public void StartHDDHum()
        {
            _hddActive = true;
            if (_hddHumClip == null || _loopSource == null) return;
            if (_loopSource.isPlaying || AudioSettings.dspTime < _humStartsAt) return;

            ScheduleHum(AudioSettings.dspTime, afterStartup: false);
        }

        /// <summary>Rattles like an old drive reading files for the next <paramref name="seconds"/>.</summary>
        public void ReadFor(float seconds)
        {
            _readingUntil = Time.time + seconds;
            _nextTickAt = Mathf.Min(_nextTickAt, Time.time);
        }

        /// <summary>Turns the seek ticks on or off. Every boot turns them back on.</summary>
        public void SetSeeking(bool seeking)
        {
            _seeking = seeking;
        }

        public void PlayHDDClick()
        {
            PlayTick(1f);
        }

        private void PlayTick(float loudness)
        {
            if (_hddClickClip == null || _tickSource == null) return;

            _tickSource.pitch = Random.Range(0.85f, 1.15f);
            _tickSource.PlayOneShot(_hddClickClip, loudness * _clickVolume * _masterVolume);
        }

        private void ScheduleHum(double startAt, bool afterStartup)
        {
            if (_humSettle != null)
                StopCoroutine(_humSettle);
            _humSettle = null;

            _hddActive = true;
            _loopSource.clip = _hddHumClip;
            _loopSource.loop = true;
            // Both clips share a recording and gain, so the hum continues at startup loudness.
            _loopSource.volume = (afterStartup ? _startupVolume : _humVolume) * _masterVolume;
            _loopSource.PlayScheduled(startAt);
            _humStartsAt = startAt;
            if (afterStartup)
                _humSettle = StartCoroutine(SettleHum(startAt));
        }

        private IEnumerator SettleHum(double startAt)
        {
            while (AudioSettings.dspTime < startAt)
                yield return null;

            float from = _loopSource.volume;
            float to = _humVolume * _masterVolume;
            for (float t = 0; t < HumSettleSeconds; t += Time.deltaTime)
            {
                _loopSource.volume = Mathf.Lerp(from, to, t / HumSettleSeconds);
                yield return null;
            }
            _loopSource.volume = to;
            _humSettle = null;
        }

        /// <summary>Loops until <see cref="StopHDDBusy"/>.</summary>
        public void PlayHDDBusy()
        {
            if (_hddBusyClip == null || _busySource == null) return;

            _busySource.clip = _hddBusyClip;
            _busySource.volume = _clickVolume * _masterVolume;
            _busySource.loop = true;
            _busySource.Play();
        }

        public void StopHDDBusy()
        {
            if (_busySource != null)
            {
                _busySource.Stop();
            }
        }

        public void PlayShutdownSound()
        {
            StopHDDHum();

            if (_shutdownClip != null && _mainSource != null)
            {
                _mainSource.PlayOneShot(_shutdownClip, _startupVolume * _masterVolume);
            }
        }

        public void SetHDDActive(bool active)
        {
            _hddActive = active;

            if (_loopSource != null)
            {
                if (active && !_loopSource.isPlaying)
                {
                    StartHDDHum();
                }
                else if (!active)
                {
                    StopHDDHum();
                }
            }
        }

        public void StopAll()
        {
            _mainSource?.Stop();
            StopHDDHum();
            _busySource?.Stop();
            _tickSource?.Stop();
            _hddActive = false;
        }

        public void SetHumVolume(float normalizedVolume)
        {
            if (_loopSource != null)
            {
                _loopSource.volume = Mathf.Clamp01(normalizedVolume) * _humVolume * _masterVolume;
            }
        }

        public void SetMasterVolume(float volume)
        {
            _masterVolume = Mathf.Clamp01(volume);

            if (_loopSource != null && _loopSource.isPlaying)
            {
                _loopSource.volume = _humVolume * _masterVolume;
            }
        }

        #endregion

        #region Private Methods

        private IEnumerator LoadMissingClips()
        {
            string folder = RATMain.ModEntry != null ? Path.Combine(RATMain.ModEntry.Path, SoundsFolder) : null;
            if (folder == null || !Directory.Exists(folder)) yield break;

            if (_hddStartupClip == null) yield return LoadClip(folder, "hdd_startup", clip => _hddStartupClip = clip);
            if (_startupPending && _hddStartupClip != null && Time.time - _startupRequestedAt < LateStartupWindow)
                PlayHDDStartup();
            _startupPending = false;

            if (_hddHumClip == null) yield return LoadClip(folder, "hdd_hum", clip => _hddHumClip = clip);
            // The hum may have been requested before its clip existed.
            if (_hddActive && _loopSource != null && !_loopSource.isPlaying) StartHDDHum();
            if (_hddClickClip == null) yield return LoadClip(folder, "hdd_click", clip => _hddClickClip = clip);
            if (_hddBusyClip == null) yield return LoadClip(folder, "hdd_busy", clip => _hddBusyClip = clip);
            if (_shutdownClip == null) yield return LoadClip(folder, "shutdown", clip => _shutdownClip = clip);
        }

        private static IEnumerator LoadClip(string folder, string name, System.Action<AudioClip> assign)
        {
            string path = Path.Combine(folder, name + ".wav");
            if (!File.Exists(path)) yield break;

            using (var request = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, AudioType.WAV))
            {
                yield return request.SendWebRequest();
                if (request.isNetworkError || request.isHttpError)
                {
                    RATMain.LogWarning($"Could not load sound {path}: {request.error}");
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(request);
                clip.name = name;
                assign(clip);
            }
        }

        private void StopHDDHum()
        {
            if (_humSettle != null)
                StopCoroutine(_humSettle);
            _humSettle = null;
            _humStartsAt = double.NegativeInfinity;
            if (_loopSource != null)
            {
                _loopSource.Stop();
            }
        }

        // Each on its own object so its MuffleFilter acts on that source alone.
        private AudioSource NewSource(string name)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(transform, false);
            var source = holder.AddComponent<AudioSource>();
            holder.AddComponent<MuffleFilter>();
            return source;
        }

        private void ConfigureSource(AudioSource source, bool loop)
        {
            if (source == null) return;

            source.spatialBlend = _spatialBlend;
            source.loop = loop;
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            source.minDistance = 0.1f;
            source.maxDistance = 5f;
        }

        #endregion
    }
}