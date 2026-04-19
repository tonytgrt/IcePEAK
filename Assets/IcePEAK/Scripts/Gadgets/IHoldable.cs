namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Implemented by any item that can live in a HandCell or BeltSlot.
    /// OnTransfer is called once per transfer, after reparenting is complete.
    /// DisplayName is the human-readable label used by hint UI.
    /// </summary>
    public interface IHoldable
    {
        void OnTransfer(CellKind from, CellKind to);
        string DisplayName { get; }
    }
}
