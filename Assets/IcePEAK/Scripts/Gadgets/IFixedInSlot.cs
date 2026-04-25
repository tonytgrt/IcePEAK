namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Marker for items that live permanently in a single belt slot and reject
    /// swap/draw/stow. <see cref="HandInteractionController"/> early-returns on
    /// any slot whose held item carries this interface, so grip-press at that
    /// slot is free to be claimed by another system (e.g. <c>DroneController</c>
    /// for the drone peek view).
    /// </summary>
    public interface IFixedInSlot
    {
        /// <summary>
        /// Hint text shown when the player hovers a slot holding this item.
        /// Should describe the alternative grip-held action (e.g. "Hold grip to scout").
        /// </summary>
        string HintText { get; }
    }
}
