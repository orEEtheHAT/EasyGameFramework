using System;
using EasyFeedbacks;
using UnityEngine;

namespace EasyCombat
{
    public sealed partial class Health
    {
        [SerializeField, Tooltip("Play assigned damage, death and healing feedback. Empty preset slots do no work.")]
        private bool feedbacksEnabled = true;
        [SerializeField] private FeedbackPreset damageFeedback;
        [SerializeField] private FeedbackPreset deathFeedback;
        [SerializeField] private FeedbackPreset healingFeedback;
        [SerializeField, Tooltip("Optional component implementing IFeedbackContextProvider, for example the owning player's device context. Preset positions and strength are preserved.")]
        private MonoBehaviour feedbackContextProvider;

        public bool FeedbacksEnabled { get => feedbacksEnabled; set => feedbacksEnabled = value; }
        public FeedbackPreset DamageFeedback { get => damageFeedback; set => damageFeedback = value; }
        public FeedbackPreset DeathFeedback { get => deathFeedback; set => deathFeedback = value; }
        public FeedbackPreset HealingFeedback { get => healingFeedback; set => healingFeedback = value; }
        public MonoBehaviour FeedbackContextProvider { get => feedbackContextProvider; set => feedbackContextProvider = value; }

        /// <summary>Assigns feedback configuration without changing FeedbacksEnabled or starting output.</summary>
        public void ConfigureFeedbacks(FeedbackPreset damage, FeedbackPreset death, FeedbackPreset healing,
            MonoBehaviour contextProvider = null)
        {
            damageFeedback = damage;
            deathFeedback = death;
            healingFeedback = healing;
            feedbackContextProvider = contextProvider;
        }

        private readonly struct CapturedHealthFeedback
        {
            internal readonly FeedbackPreset Preset;
            internal readonly FeedbackContext Context;
            internal CapturedHealthFeedback(FeedbackPreset preset, FeedbackContext context)
            { Preset = preset; Context = context; }
        }

        // Capture the committed request before death reward/wallet observers can pool or destroy Health.
        // Finishing Downed can kill with zero HP lost, so death must not depend on Damaged publication.
        private CapturedHealthFeedback CaptureDamageFeedback(in DamageResult result)
        {
            if (!feedbacksEnabled || CombatSaveSystem.IsRestoring || !this) return default;
            var preset = result.Killed ? deathFeedback ? deathFeedback : damageFeedback
                : result.AppliedAmount > 0 ? damageFeedback : null;
            if (!preset) return default;
            try
            {
                var context = new FeedbackContext(result.Damage.Point, Quaternion.identity, transform);
                context = PrepareHealthFeedback(context, feedbackContextProvider);
                return new CapturedHealthFeedback(preset, context);
            }
            catch (Exception error)
            {
                // A provider may mutate/destroy actor components; committed reward execution must still continue.
                Debug.LogException(error);
                return default;
            }
        }

        // Execute the captured intent after rewards. Do not reread Health fields, enabled state or transform.
        private void ProcessDamageFeedback(in CapturedHealthFeedback intent)
        {
            if (!intent.Preset) return;
            try
            {
                FeedbackService.Play(intent.Preset, intent.Context);
            }
            catch (Exception error) { Debug.LogException(error); }
        }

        // Called only after HP/shield commit, before Changed/Healed observers. Shield-only overheal is silent.
        private void ProcessHealingFeedback(in HealingResult result)
        {
            if (!feedbacksEnabled || !healingFeedback || result.AppliedAmount <= 0 || CombatSaveSystem.IsRestoring || !this) return;
            try
            {
                var preset = healingFeedback;
                var context = PrepareHealthFeedback(FeedbackContext.From(transform), feedbackContextProvider);
                FeedbackService.Play(preset, context);
            }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        private static FeedbackContext PrepareHealthFeedback(FeedbackContext context, MonoBehaviour contextProvider)
        {
            // Preserve the previous Combat bridge's provider policy, including assigned disabled providers.
            // It adds player defaults without replacing authored positions, strength or explicit gamepad choices.
            if (contextProvider && contextProvider is IFeedbackContextProvider provider)
                context = provider.Prepare(context);
            return context;
        }
    }
}
