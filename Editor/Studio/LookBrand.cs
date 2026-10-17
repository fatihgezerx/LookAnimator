using Veneer;
using UnityEngine;

namespace LookAnimation
{
    /// <summary>The Look Animator brand for the Veneer look: pink accent, logo and banner from <c>Editor/Brand</c>.</summary>
    internal static class LookBrand
    {
        /// <summary>The accent class the brand style sheet answers to (the exact pink, instead of a kit preset).</summary>
        internal const string AccentClass = "look-accent";

        /// <summary>The cached brand instance; use it for every Inspector of the system.</summary>
        internal static readonly VeneerBrand Value = VeneerBrand.FromAssets(
            name: "Look Animator",
            subtitle: "Head, neck and spine follow a target",
            accent: new Color32(0xD6, 0x40, 0x9F, 255),
            logoPath: "Assets/Scripts/LookAnimator/Editor/Brand/logo.png",
            bannerPath: "Assets/Scripts/LookAnimator/Editor/Brand/banner.png",
            iconFolder: null,
            styleSheetPath: "Assets/Scripts/LookAnimator/Editor/Brand/LookBrand.uss",
            accentClass: AccentClass);
    }
}
