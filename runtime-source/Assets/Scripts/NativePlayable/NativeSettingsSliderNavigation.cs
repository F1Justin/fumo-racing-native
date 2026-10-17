namespace Fumo.NativePlayable
{
    internal sealed class NativeSettingsSliderNavigation
    {
        float remaining;
        bool moved;
        public void Select() { remaining=0; }
        public void Deselect() { remaining=0; }
        public void Started() { moved=false;remaining=0; }
        public int Tick(float x,float deltaTime)
        {
            remaining-=deltaTime;
            if(remaining>0)return 0;
            if(x==0){moved=false;return 0;}
            int direction=x>0?1:-1;
            remaining=moved?.04f:.3f;moved=true;
            return direction;
        }
    }
}
