/// <summary>Single grid slot. Kept as a plain class (not struct) so Board can mutate Occupant in place.</summary>
public class Cell
{
    public Symbol Occupant;
    public bool IsEmpty => Occupant == null;
}
