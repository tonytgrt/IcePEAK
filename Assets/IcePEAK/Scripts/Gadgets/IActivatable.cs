namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Implemented by items that respond to a trigger press while held.
    /// Called by <see cref="HandInteractionController"/> on rung 3 of its
    /// priority ladder (hand holds item, no slot hovered).
    /// </summary>
    public interface IActivatable
    {
        void Activate();
    }
}
