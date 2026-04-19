using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Implemented by any object that can show a contextual hint when a hand
    /// hovers near it. The hint text is computed from the hand's current
    /// contents, so the source can return different strings depending on
    /// whether the hand is empty, holding the same item, or holding a different
    /// item.
    /// </summary>
    public interface IHintSource
    {
        /// <summary>
        /// Returns the hint string for the given hovering hand.
        /// Return null or empty to hide the hint.
        /// </summary>
        string GetHintText(HandCell hand);

        /// <summary>
        /// World-space transform the hint label should anchor to.
        /// </summary>
        Transform HintAnchor { get; }
    }
}
