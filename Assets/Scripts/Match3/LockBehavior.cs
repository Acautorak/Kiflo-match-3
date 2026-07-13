public enum LockBehavior
{
    None,       // not locked
    Permanent,  // only loses a layer when caught in a match/special clear
    Temporary   // also loses a layer automatically after a set number of player moves
}
