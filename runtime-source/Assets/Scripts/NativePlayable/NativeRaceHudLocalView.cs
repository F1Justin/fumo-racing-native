using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        Camera localViewCamera;
        int localViewRacer;
        public void BindLocalRaceView(NativeRace race, int racer, Camera camera)
        { indicatorRace = race; localViewRacer = racer; localViewCamera = camera; }
        public void DrawSharedOriginalMinimap(NativeKartMotor[] racers) => DrawOriginalMinimap(racers);
        public void DisposeLocalRaceView()
        { DisposeAuthoredUi(); canvas?.DisposeLocalCanvas(); canvas = null; }
        void BindLocalViewCanvases()
        { if (localViewCamera) { canvas?.BindLocalCamera(localViewCamera); authoredUi?.BindLocalCamera(localViewCamera); } }
        Camera HudRaceCamera => localViewCamera ? localViewCamera : indicatorRace ? indicatorRace.raceCamera : null;
        NativeKartMotor HudRelativeRacer(int offset) => indicatorRace.RaceRacerAtRelativeRank(offset, localViewCamera ? localViewRacer : 0);
    }
    public sealed partial class NativeUiCanvas
    {
        public void DisposeLocalCanvas() { if (root) UnityEngine.Object.Destroy(root); }
        public void BindLocalCamera(Camera camera)
        {
            foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 5;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = 1; canvas.sortingOrder = 113; canvas.targetDisplay = 0;
        }
    }
    public sealed partial class NativeAuthoredUi
    {
        public void BindLocalCamera(Camera camera)
        {
            var canvas = transforms["HUD/Canvas/TimeBG"].GetComponentInParent<Canvas>(true);
            foreach (var node in canvas.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 5;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = 1; canvas.sortingOrder = 113; canvas.targetDisplay = 0;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (!scaler) throw new System.InvalidOperationException("Original player HUD root CanvasScaler missing");
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800, 600); scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1; scaler.referencePixelsPerUnit = 100;
        }
    }
}
