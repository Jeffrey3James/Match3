using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using UnityEditor;

namespace StroTheGoat
{
    public static class ColorChanger
    {
        public static readonly Color Grey = new Color(180f / 255f, 180f / 255f, 180f / 255f, 1f);
        public static readonly Color Green = new Color(84f / 255f, 169f / 255f, 4f / 255f, 1f);
        public static readonly Color Blue = new Color(0f / 255f, 204f / 255f, 231f / 255f, 1f);
        public static readonly Color Gold = new Color(253f / 255f, 127f / 255f, 8f / 255f, 1f);
        public static readonly Color Purple = new Color(107f / 255f, 8f / 255f, 255f / 255f, 1f);
        public static readonly Color Red = new Color(255f / 255f, 0f / 255f, 0f / 255f, 1f);
    }

    public static class VisualElementsExtensions
    {
        public static VisualElement CreateChild(this VisualElement parent, params string[] classes)
        {
            var child = new VisualElement();
            child.AddClass(classes).AddTo(parent);
            return child;
        }

        public static T CreateChild<T>(this VisualElement parent, params string[] classes) where T : VisualElement, new()
        {
            var child = new T();
            child.AddClass(classes).AddTo(parent);
            return child;
        }

        public static T AddTo<T>(this T child, VisualElement parent) where T : VisualElement, new()
        {
            parent.Add(child);
            return child;
        }

        public static T AddClass<T>(this T visualElement, params string[] classes) where T : VisualElement
        {
            foreach (string cls in classes)
            {
                if (!string.IsNullOrEmpty(cls))
                {
                    visualElement.AddToClassList(cls);
                }
            }

            return visualElement;
        }

        public static T WithManipulators<T>(this T visualElement, IManipulator maniuplator) where T : VisualElement
        {
            visualElement.AddManipulator(maniuplator);
            return visualElement;
        }
    }

    public static class WaitExtensions
    {
        public static IEnumerator DelayerWithContinuance(float delayTime, float continueTime, System.Action callback)
        {
            yield return new WaitForSeconds(delayTime);
            callback?.Invoke();
            yield return new WaitForSeconds(continueTime);
        }
    }

    public static class CoroutineUtils
    {
        public static IEnumerator AwaitTask(Task task)
        {
            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
            }
        }
    }

    public static class UnityServicesInitializer
    {
        public static bool IsSignedIn => AuthenticationService.Instance.IsSignedIn;
        public static async Task InitializeAndSignInAsync()
        {
            try
            {
                await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log("✅ Signed in anonymously as: " + AuthenticationService.Instance.PlayerId);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("❌ Unity Services Init Failed: " + e.Message);
            }
        }
    }


    #region Timer

        public abstract class Timer
        {
            protected float initialTime;
            protected float Time { get; set; }
            public bool isRunning { get; protected set; }

            public float progress => Time / initialTime;

            public Action OnTimerStart = delegate { };
            public Action OnTimerStop = delegate { };
            public Action ForceTimerEnd = delegate { };

            protected Timer(float value)
            {
                initialTime = value;
                isRunning = false;
            }

            public void StartTimer()
            {
                Time = initialTime;
                if (!isRunning)
                {
                    isRunning = true;
                    OnTimerStart.Invoke();
                }
            }

            public void StopTimer()
            {
                if (isRunning)
                {
                    isRunning = false;
                    OnTimerStop.Invoke();
                }
            }

            public void ForceTimer()
            {
                if (isRunning)
                {
                    isRunning = false;
                }
                ForceTimerEnd.Invoke();
            }

            public void Resume() => isRunning = true;
            public void Pause() => isRunning = false;
            public abstract void Reset();

            public abstract void Tick(float deltaTime);

        }

        public class CountdownTimer : Timer
        {
            public CountdownTimer(float value) : base(value) { }

            public override void Tick(float deltaTime)
            {
                if (isRunning && Time > 0)
                {
                    Time -= deltaTime;
                }

                if (isRunning && Time <= 0)
                {
                    StopTimer();
                }
            }

            public bool IsFinished => Time <= 0;

            public override void Reset() => Time = initialTime;

            public void Reset(float newTime)
            {
                initialTime = newTime;
                Reset();
            }
        }

        public class StopwatchTimer : Timer
        {
            public StopwatchTimer() : base(0) { }

            public override void Tick(float deltaTime)
            {
                if (isRunning)
                {
                    Time += deltaTime;
                }
            }

            public override void Reset() => Time = 0;

            public float GetTime() => Time;
        }

    #endregion
    #region Time UTILS

    /// <summary>
    /// Provides utility functions for working with time, including Unix timestamps, countdown formatting,
    /// duration calculations, and safe parsing. Designed to be modular and reusable across multiple projects.
    /// </summary>
    public static class TimeUtils
    {
        /// <summary>
        /// Gets the current UTC time as a Unix timestamp (seconds since epoch).
        /// </summary>
        public static long UnixNow => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>
        /// Calculates the number of full minutes between two Unix timestamps.
        /// </summary>
        /// <param name="startUnix">The starting Unix time (in seconds).</param>
        /// <param name="endUnix">The ending Unix time (in seconds).</param>
        /// <returns>The number of minutes between the two timestamps.</returns>
        public static int MinutesBetween(long startUnix, long endUnix)
        {
            return (int)(endUnix - startUnix) / 60;
        }

        /// <summary>
        /// Returns a formatted countdown string showing the time left until a duration has passed from a start Unix timestamp.
        /// </summary>
        /// <param name="startUnixTime">The starting Unix timestamp (in seconds).</param>
        /// <param name="intervalDurationSeconds">The duration (in seconds) to count down from.</param>
        /// <param name="includeHours">If true, formats as HH:MM:SS. Otherwise MM:SS.</param>
        /// <returns>
        /// A countdown string like "12:34" or "01:12:34". Returns "00:00" if the duration has already passed.
        /// </returns>
        public static string FormatCountdown(long startUnixTime, int intervalDurationSeconds, bool includeHours = false)
        {
            long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long secondsPassed = currentUnixTime - startUnixTime;
            long secondsRemaining = Math.Max(0, intervalDurationSeconds - secondsPassed);

            TimeSpan time = TimeSpan.FromSeconds(secondsRemaining);

            return includeHours
                ? $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}"
                : $"{time.Minutes:D2}:{time.Seconds:D2}";
        }

        /// <summary>
        /// Calculates the full <see cref="TimeSpan"/> between two Unix timestamps.
        /// </summary>
        /// <param name="startUnix">The start Unix timestamp (in seconds).</param>
        /// <param name="endUnix">The end Unix timestamp (in seconds).</param>
        /// <returns>A TimeSpan representing the duration between the two timestamps.</returns>
        public static TimeSpan DurationBetween(long startUnix, long endUnix)
        {
            return TimeSpan.FromSeconds(endUnix - startUnix);
        }

        /// <summary>
        /// Attempts to parse a string as a Unix timestamp. Returns 0 if parsing fails.
        /// </summary>
        /// <param name="s">The string to parse.</param>
        /// <returns>The parsed Unix timestamp as a long, or 0 if invalid.</returns>
        public static long ParseUnixString(string s)
        {
            return long.TryParse(s, out long result) ? result : 0;
        }
    }

    /// <summary>
    /// A lightweight struct representing a snapshot of the current time, including both a <see cref="DateTime"/> and a Unix timestamp.
    /// </summary>
    public struct TimeSnapshot
    {
        /// <summary>
        /// The current system time in UTC format.
        /// </summary>
        public DateTime CurrentTime;

        /// <summary>
        /// The current time as a Unix timestamp (seconds since epoch).
        /// </summary>
        public long CurrentUnixTime;

        /// <summary>
        /// Returns a formatted string containing both the UTC DateTime and Unix timestamp.
        /// </summary>
        /// <returns>A string representation of the time snapshot.</returns>
        public override string ToString()
        {
            return $"[Local UTC TIME: {CurrentTime}, Unix: {CurrentUnixTime} ]";
        }
    }

    #endregion

    #region Dice Utils

    public static class Dice
    {
        public static int RollDice(int sides)
        {
            int diceRoll = UnityEngine.Random.Range(1, sides + 1);
            return diceRoll;
        }

        public static List<int> RollMultipleDiceOfSameType(int numberOfDice, int sides)
        {
            List<int> rolls = new List<int>();

            for (int i = 0; i < numberOfDice; i++)
            {
                int diceRoll = RollDice(sides);
                rolls.Add(diceRoll);
                Debug.Log($"Rolled a {sides}-sided dice: {diceRoll}");
            }

            return rolls;
        }

        public static int AddAllDiceOfSameType(int numberOfDice, int sides)
        {
            var rolls = RollMultipleDiceOfSameType(numberOfDice, sides);
            int sumOfDice = 0;

            foreach (int roll in rolls)
            {
                sumOfDice += roll;
            }

            Debug.Log($"Sum of all rolled {sides}-sided dice: {sumOfDice}");
            return sumOfDice;
        }

        public static List<int> RollMultipleTypesOfDifferentDice(List<(int numberOfDice, int sides)> diceConfigs)
        {
            List<int> allRolls = new List<int>();

            foreach (var (numberOfDice, sides) in diceConfigs)
            {
                allRolls.AddRange(RollMultipleDiceOfSameType(numberOfDice, sides));
            }

            return allRolls;
        }

        public static int AddAllDiceOfDifferentTypes(List<(int numberOfDice, int sides)> diceConfigs, List<int> rolls)
        {
            int totalSum = 0;
            foreach (var (numberOfDice, sides) in diceConfigs)
            {
                totalSum += AddAllDiceOfSameType(numberOfDice, sides);
            }
            Debug.Log($"Total sum of all rolled dice: {totalSum}");
            return totalSum;
        }
    }

    #endregion

    #region Editor Utils

    public static class EditorUtils
    {
        public static void CreateLabelAndConfigure(string labelName, GUIStyle guiStyle, Color color)
        {
            GUIStyle tempStyle = new GUIStyle(guiStyle);
            tempStyle.normal.textColor = color;
            GUILayout.Label(labelName, tempStyle);
        }

        public static GUIStyle CenteredStyle(int fontSize)
        {
            GUIStyle centeredStyle = new GUIStyle(EditorStyles.boldLabel);
            centeredStyle.alignment = TextAnchor.MiddleCenter;
            centeredStyle.fontSize = fontSize;
            return centeredStyle;
        }

        public static void AddSpaceToGUI(int spaceToAdd)
        {
            GUILayout.Space(spaceToAdd);
        }
    }
    #endregion
}
