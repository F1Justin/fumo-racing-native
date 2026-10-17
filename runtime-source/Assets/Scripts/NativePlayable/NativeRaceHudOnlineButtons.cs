namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public void NetworkRow(string key, bool selected = false) => OnlineElement(key)?.SelectRow(selected);
    }
}
