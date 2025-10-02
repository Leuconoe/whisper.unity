using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Class for matching commands using Longest Common Substring (LCS) and Levenshtein distance.
/// </summary>
public class CommandMatcher
{
    // Adjustable thresholds for LCS and Levenshtein
    [Range(0f, 1f)]
    public float lcsSimilarityThreshold; // Threshold for LCS (e.g., 0.6 = 60%)
    [Range(0, 10)]
    public int maxLevenshteinDistance; // Maximum allowed Levenshtein distance
    public float lcsWeight = 0.7f; // Weight for LCS
    public float levenshteinWeight = 0.3f; // Weight for Levenshtein

    private TextNormalizer textNormalizer = new TextNormalizer();

    public CommandMatcher(float lcsSimilarityThreshold, int maxLevenshteinDistance)
    {
        this.lcsSimilarityThreshold = lcsSimilarityThreshold;
        this.maxLevenshteinDistance = maxLevenshteinDistance;
    }

    /// <summary>
    /// Finds all non-overlapping commands in the transcription text.
    /// </summary>
    /// <param name="transcription">The transcription text to analyze.</param>
    /// <param name="availableCommands">List of available commands to match against.</param>
    /// <returns>List of recognized commands with their scores and positions.</returns>
    public List<CommandMatchResult> FindBestMatchingCommand(string transcription, List<string> availableCommands)
    {
        List<CommandMatchResult> matches = new List<CommandMatchResult>();
        string normalizedTranscription = textNormalizer.Normalize(RemoveWhisperArtifacts(transcription));
        int currentIndex = 0;
        bool hasMoreTextToProcess = !string.IsNullOrEmpty(normalizedTranscription);

        // Minimum command length to consider
        int minCommandLength = 2;

        // Order commands by length (descending) to prioritize longer commands
        var orderedCommands = availableCommands.OrderByDescending(c => c.Length);

        while (hasMoreTextToProcess)
        {
            string bestCommand = null;
            float bestScore = 0f;
            int bestStartIndex = -1;
            int bestEndIndex = -1;

            foreach (string command in orderedCommands)
            {
                string normalizedCommand = textNormalizer.Normalize(command);
                int commandLength = normalizedCommand.Length;

                // Only process commands that meet the minimum length requirement
                if (commandLength >= minCommandLength)
                {
                    // For short commands (length <= 5), check for prefix matches with higher tolerance
                    bool isShortCommand = commandLength <= 5;
                    float shortCommandLCSThreshold = isShortCommand ? lcsSimilarityThreshold * 0.8f : lcsSimilarityThreshold;
                    int shortCommandMaxLevenshtein = isShortCommand ? maxLevenshteinDistance + 1 : maxLevenshteinDistance;

                    // Split command into words to determine if it's multi-word
                    string[] commandWords = normalizedCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    bool isMultiWordCommand = commandWords.Length > 1;

                    if (isMultiWordCommand)
                    {
                        // Handle multi-word commands
                        int foundIndex = FindMultiWordCommand(normalizedTranscription, commandWords, maxLevenshteinDistance);

                        if (foundIndex >= 0)
                        {
                            // Extract the matched substring
                            string matchedSubstring;
                            if (foundIndex + normalizedCommand.Length <= normalizedTranscription.Length)
                            {
                                matchedSubstring = normalizedTranscription.Substring(foundIndex, normalizedCommand.Length);
                            }
                            else
                            {
                                matchedSubstring = normalizedTranscription.Substring(foundIndex);
                            }

                            // Calculate similarities
                            string lcs = FindLCS(matchedSubstring, normalizedCommand);
                            float lcsSimilarity = CalculateLCSSimilarity(lcs, normalizedCommand);
                            int levenshteinDistance = LevenshteinDistance(matchedSubstring, normalizedCommand);
                            float levenshteinSimilarity = 1f - Math.Min((float)levenshteinDistance / normalizedCommand.Length, 1f);
                            float currentScore = (lcsSimilarity * lcsWeight) + (levenshteinSimilarity * levenshteinWeight);

                            if (currentScore > bestScore)
                            {
                                bestScore = currentScore;
                                bestCommand = command;
                                bestStartIndex = currentIndex + foundIndex;
                                bestEndIndex = bestStartIndex + normalizedCommand.Length - 1;
                            }
                        }
                    }
                    else
                    {
                        // Handle single-word commands
                        int maxSubstringLength = Math.Min(normalizedTranscription.Length, commandLength + 3);
                        for (int substringLength = Math.Max(minCommandLength, commandLength - 2);
                            substringLength <= maxSubstringLength;
                            substringLength++)
                        {
                            for (int i = 0; i <= normalizedTranscription.Length - substringLength; i++)
                            {
                                string substring = normalizedTranscription.Substring(i, substringLength);

                                // Calculate similarities
                                string lcs = FindLCS(substring, normalizedCommand);
                                float lcsSimilarity = CalculateLCSSimilarity(lcs, normalizedCommand);
                                int levenshteinDistance = LevenshteinDistance(substring, normalizedCommand);

                                // Normalize Levenshtein distance by command length
                                float levenshteinSimilarity = 1f - Math.Min(
                                    (float)levenshteinDistance / Math.Max(substringLength, commandLength),
                                    1f
                                );

                                // Use adjusted thresholds for short commands
                                float currentLCSThreshold = isShortCommand ? shortCommandLCSThreshold : lcsSimilarityThreshold;
                                int currentMaxLevenshtein = isShortCommand ? shortCommandMaxLevenshtein : maxLevenshteinDistance;

                                float currentScore = (lcsSimilarity * lcsWeight) + (levenshteinSimilarity * levenshteinWeight);

                                // Check if it meets the thresholds
                                if (lcsSimilarity >= currentLCSThreshold &&
                                    levenshteinDistance <= currentMaxLevenshtein &&
                                    currentScore > bestScore)
                                {
                                    bestScore = currentScore;
                                    bestCommand = command;
                                    bestStartIndex = currentIndex + i;
                                    bestEndIndex = bestStartIndex + substringLength - 1;
                                }
                            }
                        }
                    }
                }
            }

            if (bestCommand != null)
            {
                string matchedText;
                int start = bestStartIndex - currentIndex;
                int length = bestEndIndex - bestStartIndex + 1;

                if (start >= 0 && start < normalizedTranscription.Length)
                {
                    if (start + length > normalizedTranscription.Length)
                    {
                        matchedText = normalizedTranscription.Substring(start);
                    }
                    else
                    {
                        matchedText = normalizedTranscription.Substring(start, length);
                    }
                }
                else
                {
                    matchedText = normalizedTranscription;
                }
                matches.Add(new CommandMatchResult(
                    bestCommand,
                    bestScore,
                    LevenshteinDistance(matchedText, bestCommand),
                    bestStartIndex + currentIndex
                ));

                // Remove the recognized part from the transcription
                int removeStart = bestEndIndex - currentIndex + 1;
                if (removeStart < normalizedTranscription.Length)
                {
                    normalizedTranscription = normalizedTranscription.Substring(removeStart);
                }
                else
                {
                    normalizedTranscription = string.Empty;
                }
                currentIndex = bestEndIndex + 1;
                hasMoreTextToProcess = !string.IsNullOrEmpty(normalizedTranscription);
            }
            else
            {
                hasMoreTextToProcess = false;
            }
        }

        return matches;
    }

    // <summary>
    /// Finds a multi-word command in the transcription text.
    /// </summary>
    private int FindMultiWordCommand(string text, string[] commandWords, int maxLevenshteinDistance)
    {
        string[] textWords = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        // Search for the command words sequence in the text words
        for (int i = 0; i <= textWords.Length - commandWords.Length; i++)
        {
            bool matchFound = true;
            int totalLevenshtein = 0;

            // Check each word in the command
            for (int j = 0; j < commandWords.Length; j++)
            {
                int wordLevenshtein = LevenshteinDistance(textWords[i + j], commandWords[j]);
                if (wordLevenshtein > maxLevenshteinDistance)
                {
                    matchFound = false;
                    break;
                }
                totalLevenshtein += wordLevenshtein;
            }

            if (matchFound && totalLevenshtein <= maxLevenshteinDistance * commandWords.Length)
            {
                // Calculate the start index in the original text
                int startIndex = 0;
                for (int k = 0; k < i; k++)
                {
                    startIndex += textWords[k].Length + 1; // +1 for the space
                }
                return startIndex;
            }
        }

        return -1; // Not found
    }

    /// <summary>
    /// Verify if the acumulated text contains a prefix of one of the commands.
    /// </summary>
    public bool ContainsCommandPrefix(string text, List<string> availableCommands)
    {
        foreach (var command in availableCommands)
        {
            string normalizedCommand = textNormalizer.Normalize(command);
            string normalizedText = textNormalizer.Normalize(text);

            // Si el texto acumulado contiene el inicio de algún comando, no limpiamos
            if (normalizedText.Contains(normalizedCommand.Substring(0, Math.Min(3, normalizedCommand.Length))))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Finds the Longest Common Substring (LCS) between two strings.
    /// </summary>
    /// <param name="str1">First string.</param>
    /// <param name="str2">Second string.</param>
    /// <returns>The longest common substring.</returns>
    private string FindLCS(string str1, string str2)
    {
        int m = str1.Length;
        int n = str2.Length;
        int[,] dp = new int[m + 1, n + 1];
        int maxLength = 0;
        int endIndex = 0;

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (str1[i - 1] == str2[j - 1])
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                    if (dp[i, j] > maxLength)
                    {
                        maxLength = dp[i, j];
                        endIndex = i - 1;
                    }
                }
                else
                {
                    dp[i, j] = 0;
                }
            }
        }

        return maxLength > 0 ? str1.Substring(endIndex - maxLength + 1, maxLength) : "";
    }

    /// <summary>
    /// Calculates the LCS similarity between two strings.
    /// </summary>
    /// <param name="lcs">The longest common substring.</param>
    /// <param name="command">The command string.</param>
    /// <returns>Similarity score between 0.0 and 1.0.</returns>
    private float CalculateLCSSimilarity(string lcs, string command)
    {
        if (command.Length == 0) return 0f;
        return (float)lcs.Length / command.Length;
    }

    // <summary>
    /// Calculates the Levenshtein distance between two strings.
    /// </summary>
    /// <param name="s1">First string.</param>
    /// <param name="s2">Second string.</param>
    /// <returns>The Levenshtein distance between the strings.</returns>
    private int LevenshteinDistance(string s1, string s2)
    {
        int[,] dp = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++) dp[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost
                );
            }
        }

        return dp[s1.Length, s2.Length];
    }
    
    /// <summary>
    /// Removes artifacts from whisper transcription.
    /// </summary>
    /// <param name="text">Text to remove artifacts</param>
    /// <returns>The text removing all words between [ and ], those included.</returns>
    string RemoveWhisperArtifacts(string text)
    {
        return Regex.Replace(text, @"\[.*?\]|\(.*?\)", string.Empty).Trim();
    }

}
