using System;
using System.Collections.Generic;
using UnityEngine;

namespace Story
{
    /// <summary>
    /// Manager object in the scene hierarchy providing the serialized deck of telephone calls.
    /// Can be inspected and edited directly in the Unity Inspector.
    /// Allows typing story narrative text, assigning custom portrait Sprites, and tuning choice outcomes.
    /// </summary>
    public class StoryManager : MonoBehaviour
    {
        public static StoryManager Instance { get; private set; }

        [Header("Canvas Reference")]
        [Tooltip("Reference to the StoryCardCanvas in the hierarchy.")]
        public StoryCardController storyCardCanvas;

        [Header("Camera Configuration")]
        [Tooltip("Camera used for rendering the story canvas (usually the Main Camera with SmoothCameraFollow).")]
        public Camera targetCamera;

        [Header("Story Calls Deck (Editable in Inspector)")]
        [Tooltip("Calls that ring during regular gameplay. Edit narrative, choices, sprites, and stat deltas here.")]
        public List<StoryCardData> calls = new List<StoryCardData>();

        [Header("Climactic Day 3 Finale Call")]
        [Tooltip("Triggered during Day 3 of post-max endgame.")]
        public StoryCardData climacticCall;

        [Header("Deck State")]
        [SerializeField] private int currentCallIndex = 0;

        public static StoryManager EnsureInstanceInScene()
        {
            if (Instance != null) return Instance;

            StoryManager existing = FindObjectOfType<StoryManager>(true);
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            GameObject go = new GameObject("StoryManager");
            existing = go.AddComponent<StoryManager>();
            Instance = existing;
            return existing;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            if (calls == null || calls.Count == 0)
            {
                PopulateDefaultDeck();
            }

            if (storyCardCanvas == null)
            {
                storyCardCanvas = FindObjectOfType<StoryCardController>(true);
            }

            if (targetCamera != null && storyCardCanvas != null && storyCardCanvas.targetCamera == null)
            {
                storyCardCanvas.targetCamera = targetCamera;
            }
        }

        private void Reset()
        {
            PopulateDefaultDeck();
        }

        public StoryCardData GetNextCall()
        {
            bool isEndgame = UpgradeManager.Instance != null && UpgradeManager.Instance.IsEndgameMaxed;
            if (isEndgame && RunSaveManager.HasInstance && RunSaveManager.Instance.postMaxDaysSurvived >= 3 && !RunSaveManager.Instance.finalPhoneEncounterDone)
            {
                if (climacticCall != null && !string.IsNullOrEmpty(climacticCall.id))
                {
                    return climacticCall;
                }
                return GetDefaultClimacticCall();
            }

            if (calls == null || calls.Count == 0)
            {
                PopulateDefaultDeck();
            }

            StoryCardData chosen = calls[currentCallIndex % calls.Count];
            currentCallIndex++;
            return chosen;
        }

        public void ResetDeck()
        {
            currentCallIndex = 0;
        }

        [ContextMenu("Reset to Default Deck Templates")]
        public void PopulateDefaultDeck()
        {
            calls.Clear();

            // 1. Mom
            calls.Add(new StoryCardData
            {
                id = "card_mom",
                contactName = "MOM",
                contactRole = "Incoming Call",
                portraitSprite = null,
                narrativeText = "Sweetheart, are you eating well? I transferred a little grocery money to your account. Please don't spend it on anything dangerous.",
                leftLabel = "DISMISS",
                leftChoicePrompt = "I'm busy, don't worry about me",
                leftOutcomeText = "You hung up cold. The empty apartment feels a little darker.",
                leftDepressionDelta = 8f,
                leftTirednessDelta = 0f,
                leftMoneyDelta = 0f,
                leftIsPositive = false,
                rightLabel = "THANKS MOM",
                rightChoicePrompt = "I promise I'm trying",
                rightOutcomeText = "A rare warmth washes over you. You remember you're not completely alone.",
                rightDepressionDelta = -15f,
                rightTirednessDelta = 0f,
                rightMoneyDelta = 50f,
                rightIsPositive = true
            });

            // 2. Landlord
            calls.Add(new StoryCardData
            {
                id = "card_landlord",
                contactName = "LANDLORD (HENDERSON)",
                contactRole = "Notice of Non-Payment",
                portraitSprite = null,
                narrativeText = "Rent is overdue. Pay an advance maintenance deposit of $120 today or I'm inspecting every inch of this unit Friday morning.",
                leftLabel = "STAND GROUND",
                leftChoicePrompt = "I know my tenant rights",
                leftOutcomeText = "You refuse the unlawful pressure. Heart pounding, you protect your autonomy.",
                leftDepressionDelta = 8f,
                leftTirednessDelta = 2f,
                leftMoneyDelta = 0f,
                leftIsPositive = true,
                rightLabel = "PAY MONEY",
                rightChoicePrompt = "Here's the cash, leave me alone",
                rightOutcomeText = "Money drains from your account. The relief is brief and bitter.",
                rightDepressionDelta = -5f,
                rightTirednessDelta = 0f,
                rightMoneyDelta = -120f,
                rightIsPositive = false
            });

            // 3. Old Friend
            calls.Add(new StoryCardData
            {
                id = "card_alex",
                contactName = "ALEX (OLD FRIEND)",
                contactRole = "Social",
                portraitSprite = null,
                narrativeText = "Hey man! A few of us are grilling cheap burgers at the local park. You haven't left your apartment in weeks. Come out into the sunshine!",
                leftLabel = "STAY INSIDE",
                leftChoicePrompt = "Can't make it, got too much to do",
                leftOutcomeText = "You stay curled in the dim room, watching the TV flicker in isolation.",
                leftDepressionDelta = 12f,
                leftTirednessDelta = -2f,
                leftMoneyDelta = 0f,
                leftIsPositive = false,
                rightLabel = "GO TO PARK",
                rightChoicePrompt = "I'll drop by for an hour",
                rightOutcomeText = "Fresh air, friendly laughter, and daylight remind you what life is like.",
                rightDepressionDelta = -25f,
                rightTirednessDelta = 6f,
                rightMoneyDelta = -15f,
                rightIsPositive = true
            });

            // 4. Collection Agency
            calls.Add(new StoryCardData
            {
                id = "card_debt",
                contactName = "CREDIT RECOVERY CORP",
                contactRole = "Automated Collector",
                portraitSprite = null,
                narrativeText = "Automated Notice: Settle your disputed convenience fee of $80 immediately to avoid negative score penalties.",
                leftLabel = "HANG UP",
                leftChoicePrompt = "Hang up and block number",
                leftOutcomeText = "You refuse to be swindled by automated extortion bots.",
                leftDepressionDelta = -5f,
                leftTirednessDelta = 0f,
                leftMoneyDelta = 0f,
                leftIsPositive = true,
                rightLabel = "PAY PENALTY",
                rightChoicePrompt = "Pay bogus fee to stop calls",
                rightOutcomeText = "You surrendered your hard-earned cash to grifters. The sick feeling in your stomach lingers.",
                rightDepressionDelta = 15f,
                rightTirednessDelta = 0f,
                rightMoneyDelta = -80f,
                rightIsPositive = false
            });

            // 5. Boss
            calls.Add(new StoryCardData
            {
                id = "card_boss",
                contactName = "DISPATCH SUPERVISOR",
                contactRole = "Night Shift Work",
                portraitSprite = null,
                narrativeText = "Emergency shift opening! Need someone to haul graveyard freight for 4 hours right now. Double wage ($150 cash).",
                leftLabel = "DECLINE",
                leftChoicePrompt = "I can't, my body needs rest",
                leftOutcomeText = "You chose your health over grueling exhaustion. A quiet, needed rest.",
                leftDepressionDelta = -10f,
                leftTirednessDelta = -5f,
                leftMoneyDelta = 0f,
                leftIsPositive = true,
                rightLabel = "ACCEPT SHIFT",
                rightChoicePrompt = "I'll take it, send details",
                rightOutcomeText = "You grind out the brutal hours under buzzing fluorescents. Your wallet is fatter, but your body is exhausted.",
                rightDepressionDelta = 18f,
                rightTirednessDelta = 25f,
                rightMoneyDelta = 150f,
                rightIsPositive = false
            });

            // 6. Dealer
            calls.Add(new StoryCardData
            {
                id = "card_vik",
                contactName = "VIK",
                contactRole = "Blocked Number",
                portraitSprite = null,
                narrativeText = "Got fresh imported stock delivered. Heavy stuff, guarantees you forget everything for twelve hours straight. $60 special price.",
                leftLabel = "REFUSE",
                leftChoicePrompt = "Delete number, not today",
                leftOutcomeText = "Your resolve holds steady. A quiet victory over the spiral.",
                leftDepressionDelta = -12f,
                leftTirednessDelta = 0f,
                leftMoneyDelta = 0f,
                leftIsPositive = true,
                rightLabel = "BUY STOCK",
                rightChoicePrompt = "Leave it outside door",
                rightOutcomeText = "Temporary numbing at a steep cost to your mind and sanity.",
                rightDepressionDelta = 22f,
                rightTirednessDelta = 15f,
                rightMoneyDelta = -60f,
                rightIsPositive = false
            });

            climacticCall = GetDefaultClimacticCall();
        }

        private StoryCardData GetDefaultClimacticCall()
        {
            bool isHopeful = RunSaveManager.HasInstance && RunSaveManager.Instance.positiveSecretCounter >= RunSaveManager.Instance.negativeSecretCounter;
            return new StoryCardData
            {
                id = "card_climactic_day3",
                contactName = isHopeful ? "A VOICE THROUGH THE WIRE" : "THE SCREENS ECHO",
                contactRole = isHopeful ? "A Familiar Call" : "Electric Static",
                portraitSprite = null,
                narrativeText = isHopeful
                    ? "The static calms to a gentle hum. A voice you thought you lost whispers: 'You fought through the suffocating haze. The TV screen does not have to be your whole world. Will you reach out and step into tomorrow?'"
                    : "The receiver shrieks with harsh electrical interference: 'You maxed every disc, fed every machine, yet here you stay in the dark. Are you ready to let the screen consume you?'",
                leftLabel = "GIVE UP",
                leftChoicePrompt = "Surrender to the dark",
                leftOutcomeText = "You pull the wire from the wall. The silence of the apartment presses down heavily upon your shoulders.",
                leftDepressionDelta = 40f,
                leftTirednessDelta = 20f,
                leftMoneyDelta = 0f,
                leftIsPositive = false,

                rightLabel = "REACH OUT",
                rightChoicePrompt = "Choose to step outside",
                rightOutcomeText = "A warm tear breaks through the numbness. The weight on your chest dissolves. The cycle is broken.",
                rightDepressionDelta = -40f,
                rightTirednessDelta = -15f,
                rightMoneyDelta = 0f,
                rightIsPositive = true
            };
        }
    }
}
