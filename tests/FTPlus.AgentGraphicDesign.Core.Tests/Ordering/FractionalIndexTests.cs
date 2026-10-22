using FTPlus.AgentGraphicDesign.Core.Ordering;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Ordering;

public class FractionalIndexTests
{
    [Fact]
    public void Initial_ReturnsA0()
    {
        var result = FractionalIndex.Initial();

        Assert.Equal("a0", result);
    }

    [Fact]
    public void Between_BothNull_ReturnsInitial()
    {
        var result = FractionalIndex.Between(null, null);

        Assert.Equal("a0", result);
    }

    [Fact]
    public void Between_BeforeNull_ReturnsValueBeforeAfter()
    {
        var result = FractionalIndex.Between(null, "a0");

        Assert.True(string.Compare(result, "a0", StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void Between_AfterNull_ReturnsValueAfterBefore()
    {
        var result = FractionalIndex.Between("a0", null);

        Assert.True(string.Compare(result, "a0", StringComparison.Ordinal) > 0);
    }

    [Fact]
    public void Between_ValidRange_ReturnsMidpoint()
    {
        var result = FractionalIndex.Between("a0", "b0");

        Assert.True(string.Compare(result, "a0", StringComparison.Ordinal) > 0);
        Assert.True(string.Compare(result, "b0", StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void Between_InvalidOrder_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between("b0", "a0"));
    }

    [Fact]
    public void Between_EqualValues_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between("a0", "a0"));
    }

    [Fact]
    public void Between_MultipleInsertions_MaintainsOrder()
    {
        var a = FractionalIndex.Initial();
        var b = FractionalIndex.Between(a, null);
        var c = FractionalIndex.Between(a, b);
        var d = FractionalIndex.Between(a, c);

        var ordered = new[] { a, d, c, b }.OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Equal(a, ordered[0]);
        Assert.Equal(d, ordered[1]);
        Assert.Equal(c, ordered[2]);
        Assert.Equal(b, ordered[3]);
    }

    // ---- New tests for T-07 requirements ----

    [Fact]
    public void Between_OutOfAlphabetBefore_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => FractionalIndex.Between("_0", "a0"));
        Assert.Contains("Invalid character", ex.Message);
        Assert.Contains("_", ex.Message);
    }

    [Fact]
    public void Between_OutOfAlphabetAfter_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => FractionalIndex.Between("a0", "~0"));
        Assert.Contains("Invalid character", ex.Message);
    }

    [Fact]
    public void Between_SpecialCharacters_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between("a0", "a0!"));
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between("a0@", null));
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between(null, "a0#"));
    }

    [Fact]
    public void Between_OutputAlwaysInAlphabet()
    {
        const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        var testCases = new (string? before, string? after)[]
        {
            (null, null),
            (null, "a0"),
            ("a0", null),
            ("a0", "b0"),
            ("a0", "a1"),
            ("aV", "aW"),
            ("Z", "a"),
            ("0", "1"),
            ("y", "z"),
        };

        foreach (var (before, after) in testCases)
        {
            var result = FractionalIndex.Between(before, after);

            foreach (char c in result)
            {
                Assert.Contains(c, Alphabet);
            }
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void SequentialAppends_KeyLengthGrowsLogarithmically(int count)
    {
        var keys = new List<string> { FractionalIndex.Initial() };

        for (int i = 0; i < count; i++)
        {
            var newKey = FractionalIndex.Between(keys[^1], null);
            keys.Add(newKey);
        }

        // Verify all keys are in order
        for (int i = 1; i < keys.Count; i++)
        {
            Assert.True(
                string.Compare(keys[i - 1], keys[i], StringComparison.Ordinal) < 0,
                $"Keys out of order: {keys[i - 1]} should be < {keys[i]}");
        }

        // Key length should not grow linearly with count
        // For O(n) growth: avg length would be ~ count/2
        // For O(log n) growth: avg length should be much smaller
        double avgLength = keys.Average(k => k.Length);

        // With proper increment-based append, average length should stay reasonable
        // (not O(n) like appending a constant char)
        Assert.True(avgLength < count / 4.0,
            $"Average key length {avgLength} suggests O(n) growth for {count} sequential appends");
    }

    [Fact]
    public void SequentialPrepends_MaintainsOrder()
    {
        var keys = new List<string> { FractionalIndex.Initial() };

        for (int i = 0; i < 20; i++)
        {
            var newKey = FractionalIndex.Between(null, keys[0]);
            keys.Insert(0, newKey);
        }

        // Verify all keys are in order
        for (int i = 1; i < keys.Count; i++)
        {
            Assert.True(
                string.Compare(keys[i - 1], keys[i], StringComparison.Ordinal) < 0,
                $"Keys out of order: {keys[i - 1]} should be < {keys[i]}");
        }
    }

    [Fact]
    public void Between_AdjacentCharacters_ProducesMidpoint()
    {
        // When characters are adjacent (like 'a' and 'b'), we need to extend the key
        var result = FractionalIndex.Between("a", "b");

        Assert.True(string.Compare("a", result, StringComparison.Ordinal) < 0);
        Assert.True(string.Compare(result, "b", StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void Between_IdenticalPrefix_FindsMidpointInSuffix()
    {
        var result = FractionalIndex.Between("abc", "abd");

        Assert.True(string.Compare("abc", result, StringComparison.Ordinal) < 0);
        Assert.True(string.Compare(result, "abd", StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void Between_WideGap_ReturnsMidpoint()
    {
        var result = FractionalIndex.Between("A", "z");

        Assert.True(string.Compare("A", result, StringComparison.Ordinal) < 0);
        Assert.True(string.Compare(result, "z", StringComparison.Ordinal) < 0);
        Assert.Equal(1, result.Length); // Should be able to find single-char midpoint
    }

    [Theory]
    [InlineData("0", "z")]
    [InlineData("A", "Z")]
    [InlineData("a", "z")]
    [InlineData("M", "N")]
    public void Between_VariousRanges_AlwaysSortsBetween(string before, string after)
    {
        var result = FractionalIndex.Between(before, after);

        Assert.True(string.Compare(before, result, StringComparison.Ordinal) < 0,
            $"Result {result} should be > {before}");
        Assert.True(string.Compare(result, after, StringComparison.Ordinal) < 0,
            $"Result {result} should be < {after}");
    }

    [Fact]
    public void PropertyBased_ManyRandomMidpoints_AllSortCorrectly()
    {
        var random = new Random(42); // Fixed seed for reproducibility
        const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        for (int trial = 0; trial < 100; trial++)
        {
            // Generate two random valid keys
            int len1 = random.Next(1, 5);
            int len2 = random.Next(1, 5);
            var key1 = new string(Enumerable.Range(0, len1).Select(_ => Alphabet[random.Next(Alphabet.Length)]).ToArray());
            var key2 = new string(Enumerable.Range(0, len2).Select(_ => Alphabet[random.Next(Alphabet.Length)]).ToArray());

            // Ensure key1 < key2
            if (string.Compare(key1, key2, StringComparison.Ordinal) >= 0)
            {
                (key1, key2) = (key2, key1);
            }

            if (string.Compare(key1, key2, StringComparison.Ordinal) >= 0)
            {
                // Skip if equal
                continue;
            }

            var mid = FractionalIndex.Between(key1, key2);

            Assert.True(string.Compare(key1, mid, StringComparison.Ordinal) < 0,
                $"Trial {trial}: {mid} should be > {key1}");
            Assert.True(string.Compare(mid, key2, StringComparison.Ordinal) < 0,
                $"Trial {trial}: {mid} should be < {key2}");
        }
    }

    [Fact]
    public void PropertyBased_RepeatedInsertionsBetween_AllSortCorrectly()
    {
        var keys = new List<string> { "A", "z" };

        for (int i = 0; i < 50; i++)
        {
            // Pick two adjacent keys and insert between them
            int idx = i % (keys.Count - 1);
            var newKey = FractionalIndex.Between(keys[idx], keys[idx + 1]);
            keys.Insert(idx + 1, newKey);
        }

        // Verify the entire list is sorted
        var sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(keys, sorted);
    }

    [Fact]
    public void PropertyBased_AlternatingAppendsAndPrepends_MaintainsOrder()
    {
        var keys = new List<string> { FractionalIndex.Initial() };

        for (int i = 0; i < 30; i++)
        {
            if (i % 2 == 0)
            {
                // Append
                keys.Add(FractionalIndex.Between(keys[^1], null));
            }
            else
            {
                // Prepend
                keys.Insert(0, FractionalIndex.Between(null, keys[0]));
            }
        }

        // Verify all keys are unique and sorted
        var sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(keys.Count, sorted.Distinct().Count());
        Assert.Equal(keys, sorted);
    }

    [Fact]
    public void Between_EmptyStringAfter_ThrowsForInvalidOrder()
    {
        // Empty string sorts before everything, so "a0" > "" is true
        // This means Between("a0", "") has invalid order and should throw
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between("a0", ""));
    }

    [Fact]
    public void Between_UnboundedAndEmptyKey_RejectsInsteadOfReturningOutsideBound()
    {
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between(null, ""));
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between("", null));
    }

    [Fact]
    public void Between_MinimumKey_ThrowsWhenExhausted()
    {
        // "0" is the minimum single-character key. In ordinal comparison, no valid
        // key in our alphabet can sort before "0" (shorter = empty string is not valid,
        // and there's no character less than '0' in our alphabet).
        // The algorithm should throw when this edge case is reached.
        Assert.Throws<InvalidOperationException>(() => FractionalIndex.Between(null, "0"));
    }

    [Fact]
    public void Between_PrependFromMiddle_HasPlentyOfRoom()
    {
        // Starting from "a0" (middle of alphabet), we have plenty of room to prepend
        // because 'a' > '0' by many positions
        var key = "a0";
        for (int i = 0; i < 20; i++)
        {
            key = FractionalIndex.Between(null, key);
        }

        // Should successfully create 20 keys, all smaller than "a0"
        Assert.True(string.Compare(key, "a0", StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void Between_MaximumKey_CanStillAppend()
    {
        // Even with the largest key "zzzz", we should be able to append
        var result = FractionalIndex.Between("zzzz", null);

        Assert.True(string.Compare("zzzz", result, StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void Between_VeryCloseKeys_StillFindsMidpoint()
    {
        // Keys that are very close should still allow insertion
        var before = "aaaa";
        var after = "aaab";

        var mid = FractionalIndex.Between(before, after);

        Assert.True(string.Compare(before, mid, StringComparison.Ordinal) < 0);
        Assert.True(string.Compare(mid, after, StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void Between_DifferentLengths_WorksCorrectly()
    {
        var result = FractionalIndex.Between("a", "aa");

        Assert.True(string.Compare("a", result, StringComparison.Ordinal) < 0);
        Assert.True(string.Compare(result, "aa", StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void Between_MathematicallyNonDensePair_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => FractionalIndex.Between("a", "a0"));
    }

    [Theory]
    [InlineData("a", "a00")]
    [InlineData("a", "a01")]
    [InlineData("a0", "a1")]
    [InlineData("a0z", "a1")]
    public void Between_PrefixAndAdjacentBounds_NeverEscapesRange(string before, string after)
    {
        var result = FractionalIndex.Between(before, after);

        Assert.True(string.CompareOrdinal(before, result) < 0);
        Assert.True(string.CompareOrdinal(result, after) < 0);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void DeepSubdivision_NeverExhaustsSpace(int subdivisions)
    {
        var a = "A";
        var z = "z";

        var keys = new List<string> { a, z };

        for (int i = 0; i < subdivisions; i++)
        {
            var mid = FractionalIndex.Between(keys[0], keys[1]);
            keys.Insert(1, mid);
        }

        var sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(keys, sorted);
    }

    [Fact]
    public void DeepSubdivision_KeyGrowthIsBounded()
    {
        var keys = new List<string> { "A", "z" };

        for (int i = 0; i < 100; i++)
        {
            var mid = FractionalIndex.Between(keys[0], keys[^1]);
            keys.Insert(keys.Count / 2, mid);
        }

        var maxLength = keys.Max(k => k.Length);
        Assert.True(maxLength < 20, $"Max key length {maxLength} is unexpectedly long for 100 subdivisions");
    }

    [Fact]
    public void PropertyBased_InterleaveInsertions_MaintainsOrder()
    {
        var random = new Random(12345);
        var keys = new List<string> { FractionalIndex.Initial() };

        for (int i = 0; i < 100; i++)
        {
            if (keys.Count == 1 || random.Next(3) == 0)
            {
                keys.Add(FractionalIndex.Between(keys[^1], null));
            }
            else if (random.Next(2) == 0)
            {
                keys.Insert(0, FractionalIndex.Between(null, keys[0]));
            }
            else
            {
                int idx = random.Next(keys.Count - 1);
                keys.Insert(idx + 1, FractionalIndex.Between(keys[idx], keys[idx + 1]));
            }
        }

        var sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(keys, sorted);
        Assert.Equal(keys.Count, sorted.Distinct().Count());
    }

    [Fact]
    public void AllOutputCharacters_AreInBase62Alphabet()
    {
        const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        // Generate many keys through various operations
        var keys = new List<string> { FractionalIndex.Initial() };

        // Appends
        for (int i = 0; i < 20; i++)
        {
            keys.Add(FractionalIndex.Between(keys[^1], null));
        }

        // Prepends
        for (int i = 0; i < 20; i++)
        {
            keys.Insert(0, FractionalIndex.Between(null, keys[0]));
        }

        // Midpoints
        for (int i = 0; i < 10; i++)
        {
            int idx = i % (keys.Count - 1);
            keys.Insert(idx + 1, FractionalIndex.Between(keys[idx], keys[idx + 1]));
        }

        // Verify all characters are in alphabet
        foreach (var key in keys)
        {
            foreach (char c in key)
            {
                Assert.True(Alphabet.Contains(c),
                    $"Key '{key}' contains character '{c}' which is not in the base-62 alphabet");
            }
        }
    }
}
