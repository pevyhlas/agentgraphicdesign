namespace FTPlus.AgentGraphicDesign.Core.Ordering;

/// <summary>
/// Generates fractional indices for ordering nodes without renumbering siblings.
/// Based on the algorithm from Evan Wallace (Figma) — all outputs are within
/// the closed base-62 alphabet and sort correctly with <see cref="StringComparer.Ordinal"/>.
/// </summary>
public static class FractionalIndex
{
    /// <summary>
    /// The character set for fractional indices. Uses base-62: digits, uppercase, lowercase.
    /// All characters sort correctly with ordinal string comparison.
    /// </summary>
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int Base = 62;
    private const char MinChar = '0';
    private const char MaxChar = 'z';

    /// <summary>
    /// Returns the default initial index ("a0" — roughly in the middle of the alphabet space).
    /// </summary>
    public static string Initial() => "a0";

    /// <summary>Returns whether a key is nonempty and contains only base-62 characters.</summary>
    public static bool IsValid(string? key) =>
        !string.IsNullOrEmpty(key) && key.All(c => Alphabet.IndexOf(c) >= 0);

    /// <summary>
    /// Generate an index that sorts between <paramref name="before"/> and <paramref name="after"/>.
    /// Pass null for <paramref name="before"/> to prepend, null for <paramref name="after"/> to append.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="before"/> >= <paramref name="after"/>, or if either contains
    /// characters outside the base-62 alphabet.
    /// </exception>
    public static string Between(string? before, string? after)
    {
        if (before is not null)
        {
            ValidateAlphabet(before, nameof(before));
        }

        if (after is not null)
        {
            ValidateAlphabet(after, nameof(after));
        }

        if (before is null && after is null)
        {
            return Initial();
        }

        if (before is null)
        {
            return DecrementKey(after!);
        }

        if (after is null)
        {
            return IncrementKey(before);
        }

        ValidateOrder(before, after);

        return Midpoint(before, after);
    }

    /// <summary>
    /// Generate the next key after <paramref name="key"/> by incrementing the last character
    /// or appending a midpoint character if already at max.
    /// </summary>
    private static string IncrementKey(string key)
    {
        if (key.Length == 0)
        {
            return "a0";
        }

        var chars = key.ToCharArray();
        int i = chars.Length - 1;

        // Find rightmost character that can be incremented
        while (i >= 0)
        {
            int code = CharToCode(chars[i]);
            if (code < Base - 1)
            {
                chars[i] = CodeToChar(code + 1);
                return new string(chars);
            }
            i--;
        }

        // All characters are at max — append a midpoint character
        return key + CodeToChar(Base / 2);
    }

    /// <summary>
    /// Generate a key that sorts before <paramref name="key"/> by finding a lexicographically
    /// smaller string. Strategy: decrement a character and fill remaining positions high.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown for the single-character minimum key ('0'), for which no non-empty
    /// base-62 key sorts lower.
    /// </exception>
    private static string DecrementKey(string key)
    {
        if (key.Length == 0)
        {
            return "a0";
        }

        var chars = key.ToCharArray();

        // Find rightmost character that can be decremented
        for (int i = chars.Length - 1; i >= 0; i--)
        {
            int code = CharToCode(chars[i]);
            if (code > 0)
            {
                // Decrement this character
                chars[i] = CodeToChar(code - 1);

                // For positions after this one, fill with highest character to maximize the key
                // while still being less than the original
                for (int j = i + 1; j < chars.Length; j++)
                {
                    chars[j] = CodeToChar(Base - 1);
                }

                return new string(chars);
            }
        }

        // A proper prefix sorts before the original key, including "0" < "00".
        if (key.Length > 1)
        {
            return key[..^1];
        }

        throw new InvalidOperationException(
            $"Cannot generate a key before '{key}' — the lower bound of the key space has been reached. " +
            "Consider rebalancing keys or using a longer initial key.");
    }

    /// <summary>
    /// Find the lexicographic midpoint between two keys.
    /// </summary>
    private static string Midpoint(string a, string b)
    {
        int commonLength = 0;
        while (commonLength < a.Length && commonLength < b.Length &&
               a[commonLength] == b[commonLength])
        {
            commonLength++;
        }

        if (commonLength == a.Length)
        {
            var upperSuffix = b[commonLength..];
            try
            {
                return a + MidpointBefore(upperSuffix);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"No fractional index exists strictly between '{a}' and '{b}'.", ex);
            }
        }

        int aCode = CharToCode(a[commonLength]);
        int bCode = CharToCode(b[commonLength]);
        if (bCode - aCode > 1)
        {
            var prefix = a[..commonLength];
            return prefix + CodeToChar((aCode + bCode) / 2);
        }

        // The first differing character already keeps every extension of a below b.
        return a + CodeToChar(Base / 2);
    }

    private static string MidpointBefore(string upper)
    {
        int firstCode = CharToCode(upper[0]);
        if (firstCode > 1)
        {
            return CodeToChar(firstCode / 2).ToString();
        }

        if (firstCode == 1)
        {
            return MinChar + CodeToChar(Base / 2).ToString();
        }

        if (upper.Length == 1)
        {
            throw new InvalidOperationException($"No non-empty key sorts before '{upper}'");
        }

        try
        {
            return MinChar + MidpointBefore(upper[1..]);
        }
        catch (InvalidOperationException)
        {
            // For bounds such as "00", "0" is the only key in the interval.
            return MinChar.ToString();
        }
    }

    /// <summary>
    /// Convert a character to its numeric code (0-61).
    /// </summary>
    private static int CharToCode(char c)
    {
        int index = Alphabet.IndexOf(c);
        if (index < 0)
        {
            throw new ArgumentException($"Character '{c}' is not in the fractional index alphabet");
        }
        return index;
    }

    /// <summary>
    /// Convert a numeric code (0-61) to its character.
    /// </summary>
    private static char CodeToChar(int code)
    {
        if (code < 0 || code >= Base)
        {
            throw new ArgumentOutOfRangeException(nameof(code), $"Code {code} is out of range [0, {Base})");
        }
        return Alphabet[code];
    }

    /// <summary>
    /// Validate that all characters in the key are within the alphabet.
    /// </summary>
    private static void ValidateAlphabet(string key, string paramName)
    {
        if (key.Length == 0)
        {
            throw new ArgumentException("Fractional indices cannot be empty", paramName);
        }

        foreach (char c in key)
        {
            if (Alphabet.IndexOf(c) < 0)
            {
                throw new ArgumentException(
                    $"Invalid character '{c}' in {paramName} — must use only base-62 alphabet (0-9, A-Z, a-z)",
                    paramName);
            }
        }
    }

    /// <summary>
    /// Validate that before sorts before after.
    /// </summary>
    private static void ValidateOrder(string before, string after)
    {
        if (string.Compare(before, after, StringComparison.Ordinal) >= 0)
        {
            throw new ArgumentException($"'before' ({before}) must be lexically less than 'after' ({after})");
        }
    }
}
