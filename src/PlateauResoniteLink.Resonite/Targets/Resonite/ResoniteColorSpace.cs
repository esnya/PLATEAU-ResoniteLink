using System;

using ResoniteLink;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class ResoniteColorSpace
{
    public const string SrgbProfile = "sRGB";

    public static Field_colorX CreateSrgbColorMember(ResoniteColor color)
    {
        return new Field_colorX
        {
            Value = new colorX
            {
                r = (float)color.R,
                g = (float)color.G,
                b = (float)color.B,
                a = (float)color.A,
                Profile = SrgbProfile,
            },
        };
    }

    public static color CreateLinearVertexColor(ResoniteColor color)
    {
        return new color
        {
            r = (float)ToLinearColorChannel(color.R),
            g = (float)ToLinearColorChannel(color.G),
            b = (float)ToLinearColorChannel(color.B),
            a = (float)color.A,
        };
    }

    public static double ToLinearColorChannel(double value)
    {
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
