using System;
using System.Collections.Generic;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Shared vertical de-overlap for price-level labels, used by every indicator that paints a
/// set of horizontal level lines with labels (Initial Balance, Pivots, …). Two strategies,
/// both pure (line Ys in → resolved label-centre Ys out, same order), so they unit-test:
///
/// <list type="bullet">
///   <item><see cref="Fan"/> — labels keep to a fixed side of their line (highs above, lows
///   below) and fan further away when they bunch. Use when the level line runs <i>under</i>
///   the label, so the label must clear the line vertically (Initial Balance).</item>
///   <item><see cref="Spread"/> — labels stay centred on their line until they would overlap,
///   then spread symmetrically with minimum displacement. Use when the line stops short of the
///   label (Pivots, Market Profile) so centring on the line never bisects text.</item>
/// </list>
/// </summary>
internal static class LevelLabelStacker
{
    /// <summary>
    /// Resolves label-centre Ys keeping each label on a fixed side of its line: where
    /// <paramref name="below"/> is <c>true</c> the label fans downward beneath its line, else
    /// upward above it. <paramref name="offset"/> is the clearance from the line to the nearest
    /// label centre; <paramref name="step"/> the spacing between stacked labels. Never returns a
    /// centre on the wrong side of (or on) the line, for any cluster size.
    /// </summary>
    public static double[] Fan(IReadOnlyList<double> lineY, IReadOnlyList<bool> below, double offset, double step)
    {
        int n = lineY.Count;
        var center = new double[n];
        var idx = new List<int>(n);

        // Above group: fan UPWARD. Visit bottom-most line first so we only ever push up.
        for (int i = 0; i < n; i++) if (!below[i]) idx.Add(i);
        idx.Sort((a, b) => lineY[b].CompareTo(lineY[a]));
        double last = double.PositiveInfinity;
        foreach (int i in idx)
        {
            double cy = lineY[i] - offset;
            if (cy > last - step) cy = last - step;
            last = cy;
            center[i] = cy;
        }

        // Below group: fan DOWNWARD. Visit top-most line first so we only ever push down.
        idx.Clear();
        for (int i = 0; i < n; i++) if (below[i]) idx.Add(i);
        idx.Sort((a, b) => lineY[a].CompareTo(lineY[b]));
        last = double.NegativeInfinity;
        foreach (int i in idx)
        {
            double cy = lineY[i] + offset;
            if (cy < last + step) cy = last + step;
            last = cy;
            center[i] = cy;
        }

        return center;
    }

    /// <summary>
    /// Resolves label-centre Ys so adjacent labels are at least <paramref name="minGap"/> apart
    /// while minimising total displacement from each anchor (pool-adjacent-violators). An
    /// isolated label stays exactly on its line; a bunched cluster spreads symmetrically around
    /// its centroid. Order is preserved (input must be the natural top-to-bottom level order or
    /// any order — results are assigned back by index).
    /// </summary>
    public static double[] Spread(IReadOnlyList<double> anchorY, double minGap)
    {
        int n = anchorY.Count;
        var center = new double[n];
        if (n == 0) return center;

        var ord = new int[n];
        for (int i = 0; i < n; i++) ord[i] = i;
        Array.Sort(ord, (a, b) => anchorY[a].CompareTo(anchorY[b]));

        // Each block holds a run of labels packed minGap apart; we track its label count and
        // Σ(anchor_j − j·minGap) over its local index j, so the displacement-minimising position
        // of its first slot is that sum / count. Blocks that would overlap are merged.
        var cnt = new List<int>();
        var sum = new List<double>();
        foreach (int oi in ord)
        {
            cnt.Add(1);
            sum.Add(anchorY[oi]);
            while (cnt.Count >= 2)
            {
                int k = cnt.Count - 1;
                double pPrev = sum[k - 1] / cnt[k - 1];
                double pCur = sum[k] / cnt[k];
                if (pPrev + cnt[k - 1] * minGap > pCur)
                {
                    sum[k - 1] = sum[k - 1] + sum[k] - (double)cnt[k - 1] * cnt[k] * minGap;
                    cnt[k - 1] += cnt[k];
                    cnt.RemoveAt(k);
                    sum.RemoveAt(k);
                }
                else
                {
                    break;
                }
            }
        }

        int pos = 0;
        for (int b = 0; b < cnt.Count; b++)
        {
            double p = sum[b] / cnt[b];
            for (int j = 0; j < cnt[b]; j++)
                center[ord[pos++]] = p + j * minGap;
        }
        return center;
    }
}
