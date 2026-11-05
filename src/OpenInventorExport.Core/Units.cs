using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public enum UnitKind { Length, Mass }

/// <summary>
/// Units for numeric filters. Verified on Inventor 2027: built-in numbers are stored without a unit, whatever the
/// document displays: Mass in grams, Flat Pattern Width/Length in centimetres. Exported parameters are stored as
/// text with the display unit (e.g. "2.000 in"). Area and Volume are NOT verified, so they are not listed.
/// </summary>
public static class Units
{
    // Factor to the base unit: centimetres for length, grams for mass.
    private static readonly Dictionary<string, (UnitKind Kind, double Factor)> Known =
        new Dictionary<string, (UnitKind Kind, double Factor)>(StringComparer.OrdinalIgnoreCase)
        {
            { "mm", (UnitKind.Length, 0.1) },
            { "cm", (UnitKind.Length, 1.0) },
            { "m", (UnitKind.Length, 100.0) },
            { "in", (UnitKind.Length, 2.54) },
            { "ft", (UnitKind.Length, 30.48) },
            { "g", (UnitKind.Mass, 1.0) },
            { "kg", (UnitKind.Mass, 1000.0) },
            { "lb", (UnitKind.Mass, 453.59237) },
            { "lbmass", (UnitKind.Mass, 453.59237) }   // how Inventor displays pounds of mass
        };

    // Built-in properties stored as a bare number in the base unit. Key: set|name.
    private static readonly Dictionary<string, UnitKind> BuiltIn =
        new Dictionary<string, UnitKind>(StringComparer.OrdinalIgnoreCase)
        {
            { PropertySetNames.DesignTracking + "|Mass", UnitKind.Mass },
            { PropertySetNames.DesignTracking + "|Flat Pattern Width", UnitKind.Length },
            { PropertySetNames.DesignTracking + "|Flat Pattern Length", UnitKind.Length }
        };

    private static readonly Regex Pattern = new Regex(
        @"^\s*([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s*([A-Za-z]*)\s*$", RegexOptions.CultureInvariant);

    public const string SupportedList = "mm, cm, m, in, ft, g, kg, lb";

    public static bool IsKnown(string unit) => Known.ContainsKey(unit);

    /// <summary>Reads "12.5" or "2.000 in": a number (invariant culture) and an optional unit word.</summary>
    public static bool TryParse(string text, out double number, out string unit)
    {
        number = 0;
        unit = "";
        var m = Pattern.Match(text ?? "");
        if (!m.Success) return false;
        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return false;
        unit = m.Groups[2].Value;
        return true;
    }

    /// <summary>
    /// Turns a stored value and a filter value into two numbers that can be compared.
    /// With units on the filter, both sides go to the base unit; a mismatch or an unknown unit fails with a reason.
    /// A filter without a unit compares the raw numbers (the stored value must have no unit either).
    /// </summary>
    public static bool TryCompare(string set, string property, string stored, string wanted,
        out double a, out double b, out string? problem)
    {
        a = b = 0;
        problem = null;

        if (!TryParse(stored, out double sNum, out string sUnit))
        {
            problem = $"is \"{stored}\", which is not a number";
            return false;
        }
        if (!TryParse(wanted, out double wNum, out string wUnit))
        {
            problem = $"has a filter value \"{wanted}\" that is not a number";
            return false;
        }

        UnitKind? sKind = null;
        double sFactor = 1.0;
        if (sUnit.Length > 0)
        {
            if (!Known.TryGetValue(sUnit, out var k))
            {
                problem = $"is \"{stored}\", with a unit that is not supported ({SupportedList})";
                return false;
            }
            sKind = k.Kind;
            sFactor = k.Factor;
        }
        else if (BuiltIn.TryGetValue(set + "|" + property, out var builtInKind))
        {
            sKind = builtInKind;   // already in cm or g
        }

        if (wUnit.Length == 0)
        {
            if (sUnit.Length > 0)
            {
                problem = $"is \"{stored}\", which has a unit, so the filter Value needs a unit too";
                return false;
            }
            a = sNum;
            b = wNum;
            return true;
        }

        if (!Known.TryGetValue(wUnit, out var w))
        {
            problem = $"has a filter unit \"{wUnit}\" that is not supported ({SupportedList})";
            return false;
        }
        if (sKind == null)
        {
            problem = $"is \"{stored}\", a plain number with no unit, so it cannot be compared with \"{wanted}\"";
            return false;
        }
        if (sKind.Value != w.Kind)
        {
            problem = $"is a {sKind.Value}, but the filter value \"{wanted}\" is a {w.Kind}";
            return false;
        }

        a = sNum * sFactor;
        b = wNum * w.Factor;
        return true;
    }
}