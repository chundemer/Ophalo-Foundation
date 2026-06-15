namespace OpHalo.Keep.Core.Entities.Enums;

public enum KeepRequestEventVisibility
{
    System = 1,    // lifecycle audit only; never shown to customers
    All = 2,       // visible to customer and operator
    Internal = 3   // operator-only; hidden from customer view
}
