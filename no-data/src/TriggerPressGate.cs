namespace TheInternet
{
    // One activation per physical trigger cycle, regardless of Unity's duplicate
    // Down/Up/Click notifications. Kept independent of Unity for regression tests.
    public sealed class TriggerPressGate
    {
        public bool Latched { get; private set; }
        public bool Active { get; private set; }
        public bool Begin()
        {
            if (Latched) return false;
            Latched = true; Active = true;
            return true;
        }
        public bool Complete()
        {
            bool active = Active; Active = false;
            return active;
        }
        public void Cancel() { Active = false; }
        public void Release() { Latched = false; }
        public void Reset() { Active = false; Latched = false; }
    }
}
