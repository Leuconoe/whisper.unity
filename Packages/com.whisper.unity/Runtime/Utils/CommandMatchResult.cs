/// <summary>
/// Represents the result of a command matching operation.
/// Contains information about the recognized command, its similarity score,
/// Levenshtein distance, and starting position in the original text.
/// </summary>
[System.Serializable]
public class CommandMatchResult
{
    public string command;
    public float similarityScore;
    public int levenshteinDistance;
    public int startIndex;

    /// <summary>
    /// Initializes a new instance of the CommandMatchResult class.
    /// </summary>
    /// <param name="command">The recognized command text.</param>
    /// <param name="similarityScore">The similarity score between 0.0 and 1.0.</param>
    /// <param name="levenshteinDistance">The Levenshtein distance between the recognized text and the command.</param>
    /// <param name="startIndex">The starting index of the command in the original text.</param>
    public CommandMatchResult(string command, float similarityScore, int levenshteinDistance, int startIndex)
    {
        this.command = command;
        this.similarityScore = similarityScore;
        this.levenshteinDistance = levenshteinDistance;
        this.startIndex = startIndex;
    }

    public override string ToString()
    {
        return $"Command: {command}, Score: {similarityScore:F2}, Start: {startIndex}";
    }
}
