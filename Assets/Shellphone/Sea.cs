﻿using System;
using System.Collections;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityAndroidSensors.Scripts.Utils.SmartVars;
using UnityEngine;
using VibeUtils;
using Random = UnityEngine.Random;

namespace Shellphone
{
    public class Sea : MonoBehaviour
    {
        private float targetMood;
        public SeaInfo info;
        public Coral prefabCoral;
        private float swayIndex;
        public MeshRenderer seaRenderer;
        public Animator ringAnimator;

        [ProgressBar("Health", 1f, ProgressBarColor.Green)] public float health = 1f;

        [ReadOnly] public bool isDead;
        [ReadOnly] public bool isCharging;
        public float healthPerSecondModifier;
        public AnimationCurve healthRateCurve;
        public float chanceToStartNewCoral = 1f;


        [Header("Mood logic")]
        [Slider(0f, 1f)] public float mood;
        [Slider(0f, 1f)] public float moodLimit;
        [Slider(0.1f, 5f)] public float moodChangeFactor;
        public MoodInfo sleepyMoodInfo, chirpyMoodInfo, moodInfo;
        public CurvePlayer warningVFX;


        [Header("Damage by movement logic")]
        public float movementThreshold;
        [ReadOnly] public float damageCooldown;
        public float damageInterval;
        public float damageAmount;
        [ReadOnly] public bool damageMode;
        public bool damageDurationFromFXCurve;
        public CurvePlayer damageVFX;


        [Header("Sensors wiring")]
        public Vector3Var magneticData;
        public Vector3Var accelerationData;
        public FloatVar lightSensorData;
        public FloatVar proximityData;


        [Header("Debug controls")]
        public Vector2 debugGravity;
        public float debugLight;
        [Slider(0f, 8f)] public float debugProximity;
        [Slider(0f, 1f)] public float debugBatteryLevel;
        public bool debugCharging;
        private bool justChangedAnimation;

        void Start()
        {
            Input.gyro.enabled = true;
            justChangedAnimation = false;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (damageDurationFromFXCurve) damageInterval = damageVFX.duration + 0.2f;
        }


        void Update()
        {
            UpdateHealth();
            if (isDead)
            {
                if (Camera.main.backgroundColor != Color.black)
                {
                    seaRenderer.sharedMaterial.SetColor("_Color", new Color(0.1f, 0.1f, 0.1f));
                    Camera.main.backgroundColor = Color.black;
                    StartCoroutine(EndGame());
                }
                return;
            }
            // game logic
            HandleProximity();
            // shake detection changing health
            DetectMovement();
            UpdateMood();
            UpdateChanceOfNewCoral();
            if (WillStartNewCoral())
            {
                chanceToStartNewCoral = 0f;
                CreateCoral();
            }
            // visual update
            swayIndex = Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * info.seaBPM / 60f));
            var healthColor = info.healthGradientFg.Evaluate(health);
            Camera.main.backgroundColor = info.healthGradientBg.Evaluate(health); ;
            healthColor.a = Mathf.Clamp01(0.05f + swayIndex * 0.2f);
            seaRenderer.sharedMaterial.SetColor("_Color", healthColor);
            seaRenderer.sharedMaterial.SetTextureOffset("_MainTex", new Vector2(Time.realtimeSinceStartup, swayIndex) * 0.05f);
            seaRenderer.sharedMaterial.SetTextureScale("_MainTex", Vector2.one * (5f + swayIndex * .1f));
        }


        private IEnumerator EndGame()
        {
            yield return new WaitForSeconds(60f);
            Application.Quit();
        }


        private void DetectMovement()
        {
            float accelerationMag = 0f;
#if UNITY_EDITOR
            accelerationMag = Input.acceleration.sqrMagnitude;
#else
            accelerationMag = accelerationData.value.sqrMagnitude;
#endif

            if (accelerationMag > movementThreshold)
            {
                if (damageCooldown <= 0f)
                {
                    damageMode = true;
                    health -= damageAmount;
                    damageCooldown = damageInterval;
                    damageVFX.Play();
                    ringAnimator.SetTrigger("StartBlinking");
                    ringAnimator.speed = 3f;
                    StartCoroutine(AnimationChangeDelay());
                }
            }
            else
            {
                // only releases vibration after finishing cooldown
                if (damageCooldown <= 0f)
                {
                    damageMode = false;
                    if (damageVFX.isPlaying) damageVFX.Stop();
                }
            }
            damageCooldown -= Time.deltaTime;
        }


        private void UpdateHealth()
        {
            if (isDead)
            {
                return;
            }
            float batteryLevel = 0f;
#if UNITY_EDITOR
            batteryLevel = debugBatteryLevel;
            isCharging = debugCharging;
#else
            batteryLevel = SystemInfo.batteryLevel;
            isCharging = (SystemInfo.batteryStatus == BatteryStatus.Charging);
#endif
            float applyRate = (isCharging) ? 1f : healthRateCurve.Evaluate(batteryLevel);
            health = Mathf.Lerp(health, health + applyRate, Time.deltaTime * healthPerSecondModifier);
            health = Mathf.Clamp01(health);
            if (health <= 0f)
            {
                isDead = true;
            }
        }


        private void HandleProximity()
        {
            // skip if is giving feedback on damage, which is higher priority
            if (damageMode)
            {
                return;
            }
            float proximity = 0f;
#if UNITY_EDITOR
            proximity = debugProximity;
#else
            proximity = proximityData.value;
#endif

            if (proximity >= 0.1f)
            {
                // it's ok
                warningVFX.Stop();
            }
            else if (!warningVFX.isPlaying)
            {
                warningVFX.Play();
                print("LET ME GO");
                ringAnimator.SetTrigger("StartBlinking");
                ringAnimator.speed = 1f;
                StartCoroutine(AnimationChangeDelay());
            }
        }


        private void UpdateMood()
        {
#if UNITY_EDITOR
            targetMood = Utils.Remap(debugLight, 0f, 1000f, 0f, 1f); ;
#else
            targetMood = Utils.Remap(lightSensorData.value, 0f, 1000f, 0f, 1f);
#endif
            mood = Mathf.Lerp(mood, targetMood, Time.deltaTime * moodChangeFactor);
            if (mood <= moodLimit)
            {
                // set sleepy
                moodInfo = sleepyMoodInfo;
                if (!ringAnimator.GetCurrentAnimatorStateInfo(0).IsName("Halves_Base") && !justChangedAnimation)
                {
                    ringAnimator.SetTrigger("StartHalves");
                    StartCoroutine(AnimationChangeDelay());
                    ringAnimator.speed = mood * 0.7f;
                }
            }
            else
            {
                // set chirpy
                moodInfo = chirpyMoodInfo;
                if (!ringAnimator.GetCurrentAnimatorStateInfo(0).IsName("Pulse_Base") && !justChangedAnimation)
                {
                    ringAnimator.SetTrigger("StartPulse");
                    StartCoroutine(AnimationChangeDelay());
                    ringAnimator.speed = mood;
                }
            }
        }

        private IEnumerator AnimationChangeDelay()
        {
            justChangedAnimation = true;
            yield return new WaitForSeconds(2f);
            justChangedAnimation = false;
        }

        public void CreateCoral(Coral parent = null)
        {
            Coral coral;
            if (parent == null)
            {
                Debug.Log("START NEW ROOT CORAL");
                coral = GameObject.Instantiate<Coral>(prefabCoral, LocateNewCoral(), Quaternion.identity);
                coral.depthLevel = 0;
                var random = (Vector2)Random.insideUnitCircle.normalized;
                Vector2 gravity = new Vector2();
#if UNITY_EDITOR
                gravity = debugGravity.normalized;
#elif UNITY_ANDROID
            gravity = new Vector2(Input.gyro.gravity.x, Input.gyro.gravity.y).normalized;
#endif
                var up = random + gravity * 2f;
                coral.transform.up = up.normalized;
            }
            else
            {
                // Debug.Log("NEW CORAL BRANCH");
                coral = GameObject.Instantiate<Coral>(prefabCoral, Vector2.zero, Quaternion.identity);
                LocateCoralPart(coral, parent);
                coral.depthLevel = parent.depthLevel + 1;
                coral.parent = parent;
                parent.DropSeed();
            }
            coral.sea = this;
        }

        private void LocateCoralPart(Coral coral, Coral parent)
        {
            var parentDirection = Vector2.up;
            var randomVector = (Vector2)Random.insideUnitCircle.normalized;
            var pos = parentDirection + randomVector;
            coral.transform.position = pos.normalized * 0.5f;
            coral.transform.SetParent(parent.transform, false);
        }

        private Vector3 LocateNewCoral()
        {
            return Random.insideUnitCircle * Random.Range(2f, 10f);
        }

        bool WillStartNewCoral()
        {
            return (chanceToStartNewCoral >= Random.value);
        }

        void UpdateChanceOfNewCoral()
        {
            info.CalculateAndSetChanceToStartNewCoral(health);
            // this effects a cooldown
            chanceToStartNewCoral = Mathf.Lerp(chanceToStartNewCoral, info.chanceToStartNewCoral, Time.deltaTime / info.newCoralCooldown);
        }
    }

}