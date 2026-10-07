#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace TranscribeCppSharp.Performance;

/// <summary>
/// Orders a list of models for display, keeping the speed ranking honest.
/// </summary>
/// <remarks>
/// Separate from any view so the rule can be tested on its own. It decides which
/// numbers may legitimately be compared with which, and getting that wrong is
/// worse than having no ordering at all: a ranking that silently mixes machines,
/// or invents a position for a model that was never timed, is a claim nobody
/// checked.
/// </remarks>
public static class ModelBenchmarkOrder
{
    /// <summary>
    /// Puts the rows measured on this machine first, ordered by real-time factor,
    /// then everything else alphabetically.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="rows">Every visible row.</param>
    /// <param name="alias">Reads a row's model alias.</param>
    /// <param name="benchmarks">Timings by alias, possibly measured elsewhere.</param>
    /// <param name="machineKey">The key a timing must carry to be comparable.</param>
    /// <param name="fastestFirst">Ascending by real-time factor, or descending.</param>
    /// <returns>The rows, ordered. The same instances, not copies.</returns>
    /// <remarks>
    /// A model that was never timed, or whose timing came from another machine,
    /// gets no position among the measured ones. There is no honest way to rank
    /// it: putting it first invents a speed, putting it last invents a slowness,
    /// and interleaving it invents both. Those rows follow, in name order, so the
    /// list never shows a dash where a number should be.
    /// </remarks>
    public static List<T> BySpeed<T>(
        IEnumerable<T> rows,
        Func<T, string> alias,
        IReadOnlyDictionary<string, ModelBenchmark> benchmarks,
        string machineKey,
        bool fastestFirst)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(benchmarks);
        ArgumentException.ThrowIfNullOrEmpty(machineKey);

        List<T> measured = new();
        List<T> unmeasured = new();

        foreach (T row in rows)
        {
            if (benchmarks.TryGetValue(alias(row), out ModelBenchmark? result)
                && string.Equals(result.MachineKey, machineKey, StringComparison.Ordinal))
            {
                measured.Add(row);
            }
            else
            {
                unmeasured.Add(row);
            }
        }

        measured.Sort((left, right) =>
        {
            double a = benchmarks[alias(left)].Rtf;
            double b = benchmarks[alias(right)].Rtf;
            return fastestFirst ? a.CompareTo(b) : b.CompareTo(a);
        });

        unmeasured.Sort((left, right) => string.CompareOrdinal(alias(left), alias(right)));

        measured.AddRange(unmeasured);
        return measured;
    }

    /// <summary>Orders rows alphabetically by alias.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="rows">Every visible row.</param>
    /// <param name="alias">Reads a row's model alias.</param>
    /// <param name="ascending">False for Z to A. Optional, so callers written
    /// before direction existed keep the order they had.</param>
    /// <returns>The rows, ordered.</returns>
    public static List<T> ByName<T>(IEnumerable<T> rows, Func<T, string> alias, bool ascending = true)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(alias);
        List<T> ordered = rows.OrderBy(alias, StringComparer.Ordinal).ToList();

        // The exact mirror rather than a second ordering: descending is defined as
        // the reverse of ascending, so the two can never disagree about ties.
        if (!ascending)
        {
            ordered.Reverse();
        }

        return ordered;
    }

    /// <summary>Orders rows by download size, smallest first.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="rows">Every visible row.</param>
    /// <param name="alias">Reads a row's model alias.</param>
    /// <param name="sizeBytes">Reads a row's download size.</param>
    /// <param name="ascending">False for largest first. Optional, so callers
    /// written before direction existed keep the order they had.</param>
    /// <returns>The rows, ordered, ties broken by name.</returns>
    public static List<T> BySize<T>(IEnumerable<T> rows, Func<T, string> alias, Func<T, long> sizeBytes, bool ascending = true)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(sizeBytes);
        List<T> ordered = rows
            .OrderBy(sizeBytes)
            .ThenBy(alias, StringComparer.Ordinal)
            .ToList();

        if (!ascending)
        {
            ordered.Reverse();
        }

        return ordered;
    }
}
