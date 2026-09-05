#nullable enable

using System;
using System.Collections.Generic;
using Kor.Operations.EngineeringTools.Dxf;


namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// The numbers that decide what a drawing's linework becomes, as SETTINGS rather than literals.
    /// </summary>
    /// <remarks>
    /// Every defect found reading drawings this session was a convention compiled into a binary. A
    /// metric-only size pattern read one of five KOR jobs. A 2.5 aspect ratio discarded 31168's
    /// commonest parkade column — 54 shapes at exactly 14x36 on one sheet — because that job draws
    /// PC01 at 14" x 36" and the limit was written for squarer columns. A 260-point heading scope fit
    /// one sheet size and silently returned nothing for a larger one.
    ///
    /// The DXF-to-ETABS side already solved this: <see cref="PlanClassificationOptions"/> is
    /// populated from KorStandards through 62 `dxf.*` keys, and a job that draws differently states
    /// so instead of being misread. The PDF intake path had none, so these are its keys, in the same
    /// shape and read through the same plumbing.
    ///
    /// ⚠ EVERY DEFAULT HERE IS THE VALUE THE CODE ALREADY USED. Nothing changes behaviour by being
    /// moved; a job that says nothing gets exactly what it got before. The point is that a job CAN
    /// say something, and that the number is findable when a sheet reads wrong.
    ///
    /// ⚠ These are read with <see cref="RuleSettings.Load"/>, not LoadRequired. A missing rule stops
    /// a DXF-to-ETABS production run by design, because that pipeline's numbers were argued over one
    /// at a time. These have not been, and refusing to read a drawing because nobody has banked
    /// a threshold yet would be a worse failure than using the default.
    /// </remarks>
    public sealed record PdfIntakeOptions(
        double SlabMinDiagonalMm,
        double LineMinLengthMm,
        double ColumnMaxSizeMm,
        double ColumnMinDimMm,
        double ColumnMaxAspect,
        double AgreementToleranceMm,
        double AgreementLabelReachMm)
    {
        /// <summary>The defaults, which are what the code did before any of this was settable.</summary>
        public static PdfIntakeOptions Default => new(
            SlabMinDiagonalMm:     PdfToSafeConstants.DefaultSlabMinDiagonalMm,
            LineMinLengthMm:       PdfToSafeConstants.DefaultLineMinLengthMm,
            ColumnMaxSizeMm:       1500.0,
            ColumnMinDimMm:        200.0,
            ColumnMaxAspect:       GeometryFilterService.DefaultMaxColumnAspect,
            AgreementToleranceMm:  25.0,
            AgreementLabelReachMm: 1500.0);

        public const string Prefix = "dxf.pdf";

        /// <summary>
        /// A column's SHAPE is one convention, shared with the DXF-to-ETABS side.
        /// </summary>
        /// <remarks>
        /// How slender a column may be does not depend on whether the drawing arrived as PDF or CAD,
        /// and KorStandards already states it: `dxf.max-column-aspect` = 3.0, replay-verified, on the
        /// authority of ETABS/e2k. Giving the PDF path its own `dxf.pdf.column-max-aspect` would be a
        /// second definition of one convention. Adopting 3.0 in place of an invented 3.2 changed one
        /// sheet by two shapes and no coverage anywhere.
        ///
        /// ⛔ AND THE COLUMN SIZE BOUNDS ARE **NOT** SHARED, THOUGH THE SAME KEYS EXIST. This was
        /// tried and measured on 2026-09-02. `dxf.min-column-size` 6in and `dxf.max-column-size`
        /// 132in are the DXF side's PLAUSIBILITY check, because there the LAYER already says whether
        /// a shape is a column: SLABEDG or _COL. PdfToSafe has no layers, so the same numbers are
        /// doing a different job — the size window IS the discriminator, and
        /// <see cref="GeometryFilterService.Classify"/> gives a shape to the column branch or the
        /// slab branch on exactly this test.
        ///
        /// Adopting them raised the ceiling from 1500mm to 3353mm and slabs were swallowed whole:
        ///
        ///     31130 p13   slabs 45 -> 23      31168 p11   slabs 67 -> 47
        ///     31138 p11   slabs 102 -> 58     over-detection 2.6x -> 4.3x
        ///
        /// with coverage IDENTICAL on every sheet — not one additional real column was found. One
        /// rule name, two meanings, because one pipeline has layers and the other does not.
        /// </remarks>
        public const string SharedMaxColumnAspect = "dxf.max-column-aspect";

        public static IReadOnlyList<string> SettingKeys { get; } =
        [
            SharedMaxColumnAspect,
            $"{Prefix}.column-max-size-mm",
            $"{Prefix}.column-min-dim-mm",
            $"{Prefix}.slab-min-diagonal-mm",
            $"{Prefix}.line-min-length-mm",
            $"{Prefix}.agreement-tolerance-mm",
            $"{Prefix}.agreement-label-reach-mm",
        ];

        /// <summary>Overlay whatever KorStandards states; anything unbanked keeps its default.</summary>
        public static PdfIntakeOptions ApplyRules(
            PdfIntakeOptions options,
            IReadOnlyDictionary<string, RuleSetting> settings)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(settings);

            return options with
            {
                SlabMinDiagonalMm     = settings.ValueOr($"{Prefix}.slab-min-diagonal-mm", options.SlabMinDiagonalMm),
                LineMinLengthMm       = settings.ValueOr($"{Prefix}.line-min-length-mm", options.LineMinLengthMm),
                ColumnMaxSizeMm       = settings.ValueOr($"{Prefix}.column-max-size-mm", options.ColumnMaxSizeMm),
                ColumnMinDimMm        = settings.ValueOr($"{Prefix}.column-min-dim-mm", options.ColumnMinDimMm),
                // shape is shared; the size window is not — see the remarks on SharedMaxColumnAspect
                ColumnMaxAspect       = settings.ValueOr(SharedMaxColumnAspect, options.ColumnMaxAspect),
                AgreementToleranceMm  = settings.ValueOr($"{Prefix}.agreement-tolerance-mm", options.AgreementToleranceMm),
                AgreementLabelReachMm = settings.ValueOr($"{Prefix}.agreement-label-reach-mm", options.AgreementLabelReachMm),
            };
        }

        /// <summary>
        /// The options a job states, or the defaults when no connection is given or it cannot be read.
        /// </summary>
        /// <remarks>
        /// Reading drawings is not a production publish, so an unreachable KorStandards degrades to
        /// the defaults rather than refusing. The caller is told which happened.
        /// </remarks>
        public static (PdfIntakeOptions Options, string Source) For(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return (Default, "defaults (no --rules-db given)");

            try
            {
                return (ApplyRules(Default, RuleSettings.Load(connectionString)), "KorStandards");
            }
            catch (Exception ex)
            {
                return (Default, $"defaults (KorStandards unreadable: {ex.GetType().Name})");
            }
        }
    }
}
