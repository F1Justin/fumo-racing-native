using UnityEngine;
namespace Fumo.NativePlayable {
internal static class NativeKartAnimationReference {
internal const string Source = "Original serialized idle/normalspeed/highspeed vector curves; independent cubic Hermite evaluation";
internal static Vector3 Evaluate(string clip, string path, string kind, float t) {
switch (clip + "|" + path + "|" + kind) {
case "idle||euler": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.25f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "idle|Kart|euler": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.25f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "idle|AccelPivot|euler": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.25f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "idle||position": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,float.NegativeInfinity,0f,float.NegativeInfinity,0.1f,0f,0.005f,0f,0f,-4.4703485E-09f,0f,float.NegativeInfinity,0f,float.NegativeInfinity,0.25f,0f,0f,0f,0f,-1.2715657E-08f,0f,0f,0f,0f},t);
case "idle|Kart|position": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.25f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "idle|AccelPivot|position": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.25f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "idle||scale": return Sample(new float[] {0f,1f,1f,1f,0f,0f,0f,float.NegativeInfinity,0f,float.NegativeInfinity,0.1f,1f,1.0143f,1f,0f,-1.2785186E-08f,0f,float.NegativeInfinity,0f,float.NegativeInfinity,0.25f,1f,1f,1f,0f,-2.7139976E-08f,0f,0f,0f,0f},t);
case "idle|AccelPivot|scale": return Sample(new float[] {0f,1f,1f,1f,0f,0f,0f,0f,0f,0f,0.25f,1f,1f,1f,0f,0f,0f,0f,0f,0f},t);
case "idle|Kart|scale": return Sample(new float[] {0f,1f,1f,1f,0f,0f,0f,0f,0f,0f,0.25f,1f,1f,1f,0f,0f,0f,0f,0f,0f},t);
case "normalspeed||euler": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.2f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "normalspeed|AccelPivot|euler": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.2f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "normalspeed|Kart|euler": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.2f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "normalspeed||position": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity,0.083333336f,0f,0f,0f,0f,0f,0f,float.NegativeInfinity,0f,float.NegativeInfinity,0.2f,0f,-0.008f,0f,0f,-2.0500755E-08f,0f,0f,0f,0f},t);
case "normalspeed|AccelPivot|position": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.2f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "normalspeed|Kart|position": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.2f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "normalspeed||scale": return Sample(new float[] {0f,1f,1f,1f,0f,0f,0f,float.NegativeInfinity,0f,float.NegativeInfinity,0.083333336f,1f,1.0143f,1f,0f,-3.0684447E-08f,0f,float.NegativeInfinity,0f,float.NegativeInfinity,0.2f,1f,1f,1f,0f,3.6509668E-08f,0f,0f,0f,0f},t);
case "normalspeed|Kart|scale": return Sample(new float[] {0f,1f,1f,1f,0f,0f,0f,0f,0f,0f,0.2f,1f,1f,1f,0f,0f,0f,0f,0f,0f},t);
case "normalspeed|AccelPivot|scale": return Sample(new float[] {0f,1f,1f,1f,0f,0f,0f,0f,0f,0f,0.2f,1f,1f,1f,0f,0f,0f,0f,0f,0f},t);
case "highspeed||euler": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.33333334f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "highspeed|AccelPivot|euler": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.33333334f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "highspeed|Kart|euler": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.33333334f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "highspeed||position": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,float.NegativeInfinity,float.NegativeInfinity,0.033333335f,-0.019f,0f,0f,-7.5946296E-08f,0f,0f,0f,float.NegativeInfinity,float.NegativeInfinity,0.05f,0f,0f,0f,1.3799998f,0f,0f,1.38f,0f,float.NegativeInfinity,0.083333336f,0.05f,-0.0059259264f,0f,4.3272982E-07f,-0.15999994f,0f,0f,-0.16000001f,float.NegativeInfinity,0.1f,0f,-0.008f,0f,-1.0350004f,-4.967056E-09f,0f,-1.035f,0f,float.NegativeInfinity,0.15f,-0.019f,-0.0012500011f,-0f,2.810359E-07f,0.07500011f,0f,0f,0.07500005f,float.NegativeInfinity,0.16666667f,0f,0f,0f,-8.130072E-07f,5.1657585E-09f,0f,0f,0f,float.NegativeInfinity,0.18333334f,-0.025f,-0.0020740742f,0f,-1.295409E-07f,-0.16000001f,0f,0f,-0.16000001f,float.NegativeInfinity,0.21666667f,0f,-0.008f,0f,0.9300001f,4.589556E-08f,0f,0.93000007f,0f,float.NegativeInfinity,0.25f,0.037f,-0.0040000007f,0f,-4.609425E-07f,0.120000005f,0f,0f,0.12000001f,float.NegativeInfinity,0.28333333f,0f,0f,0f,-1.3050001f,-3.2285836E-08f,0f,-1.3050001f,0f,float.NegativeInfinity,0.31666666f,-0.05f,-0.0059259236f,0f,-2.9504307E-07f,-0.15999997f,0f,0f,-0.15999997f,float.NegativeInfinity,0.33333334f,0f,-0.008f,0f,4.4190614E-07f,-7.947411E-09f,0f,0f,0f,0f},t);
case "highspeed|AccelPivot|position": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.33333334f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "highspeed|Kart|position": return Sample(new float[] {0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0.33333334f,0f,0f,0f,0f,0f,0f,0f,0f,0f},t);
case "highspeed||scale": return Sample(new float[] {0f,1f,1f,1.0545f,0f,0f,0f,float.NegativeInfinity,0f,0f,0.05f,1f,1.0143f,1f,0f,-2.5570372E-08f,4.0262935E-07f,float.NegativeInfinity,0f,0f,0.1f,1f,1f,1.0553f,0f,2.5570372E-08f,5.114675E-07f,float.NegativeInfinity,0f,0f,0.16666667f,1f,1.0143f,1f,0f,9.171728E-09f,5.6120193E-08f,float.NegativeInfinity,0f,0f,0.21666667f,1f,1f,1.0553f,0f,8.845333E-08f,-9.777544E-07f,float.NegativeInfinity,0f,0f,0.28333333f,1f,1.0143f,1f,0f,4.1718518E-10f,8.52342E-09f,float.NegativeInfinity,0f,0f,0.33333334f,1f,1f,1.0553f,0f,-4.863797E-08f,-8.192055E-07f,0f,0f,0f},t);
case "highspeed|AccelPivot|scale": return Sample(new float[] {0f,1f,1f,1f,0f,0f,0f,0f,0f,0f,0.33333334f,1f,1f,1f,0f,0f,0f,0f,0f,0f},t);
case "highspeed|Kart|scale": return Sample(new float[] {0f,1f,1f,1f,0f,0f,0f,0f,0f,0f,0.33333334f,1f,1f,1f,0f,0f,0f,0f,0f,0f},t);
default: throw new System.ArgumentException("Unknown source curve");
}}
static Vector3 Sample(float[] k,float t) {
int n=k.Length/10;
if(t<=k[0]) return new Vector3(k[1],k[2],k[3]);
for(int i=0;i<n-1;i++) { int a=i*10,b=a+10;if(t>k[b])continue;
float d=k[b]-k[a],u=(t-k[a])/d;var v=Vector3.zero;
for(int c=0;c<3;c++) {float p=k[a+1+c],q=k[b+1+c],m=k[a+7+c],z=k[b+4+c];
v[c]=float.IsInfinity(m)||float.IsInfinity(z)?p:(2*u*u*u-3*u*u+1)*p+(u*u*u-2*u*u+u)*d*m+(-2*u*u*u+3*u*u)*q+(u*u*u-u*u)*d*z;}
return v;}
int last=(n-1)*10;return new Vector3(k[last+1],k[last+2],k[last+3]);
}
}}
