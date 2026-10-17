using System;using UnityEngine;
namespace Fumo.NativePlayable {
[DefaultExecutionOrder(-10000)] public sealed class NativeModuleStructurePhysicsDriver:MonoBehaviour {
 NativeKartMotor[] motors;string vehicle;int[] stages;Vector3[] road,grass;
 public void Configure(NativeKartMotor[] targets,string selected){motors=targets;vehicle=selected;stages=new int[targets.Length];road=new Vector3[targets.Length];grass=new Vector3[targets.Length];
 for(int i=0;i<targets.Length;i++){road[i]=new Vector3(20000+i*12000,1000,20000);grass[i]=road[i]+new Vector3(5000,0,0);Surface(road[i],false);Surface(grass[i],true);var m=targets[i];m.UseFixedVerificationClock=true;m.enabled=true;m.driving=true;m.AIDriftActive=false;Place(m,selected=="genji"?grass[i]:road[i]);} }
 void Surface(Vector3 origin,bool grassy){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=grassy?"NativeInjectedGrass":"NativeInjectedRoad";g.transform.SetParent(transform);g.transform.position=origin+Vector3.down*.5f;g.transform.localScale=new Vector3(4000,1,4000);if(grassy)g.tag="Grass";}
 void Place(NativeKartMotor m,Vector3 origin){var c=m.GetComponent<CharacterController>();c.enabled=false;m.transform.SetPositionAndRotation(origin+Vector3.up*2,Quaternion.identity);c.enabled=true;Physics.SyncTransforms();}
 void FixedUpdate(){if(motors==null)return;for(int i=0;i<motors.Length;i++){var m=motors[i];var fx=m.GetComponent<NativeVehicleModuleEffects>();m.throttle=vehicle=="shoppingcart"?1:0;m.braking=vehicle=="shoppingcart";m.steering=vehicle=="shoppingcart"?.2f:0;m.drifting=vehicle=="shoppingcart";m.jump=false;
 if(vehicle=="shoppingcart"&&fx.records.Exists(x=>x.effect=="Overheat")){m.throttle=0;m.braking=false;m.drifting=false;m.steering=0;}
 if(vehicle=="broom"&&stages[i]==0&&m.Grounded&&m.ModuleReady){m.jump=true;stages[i]=1;}
 if(vehicle=="genji"){
 if(stages[i]==0&&m.Grounded&&m.OnGrass&&m.ModuleStage==1){Place(m,road[i]);stages[i]=1;}
 else if(stages[i]==1&&fx.records.Exists(x=>x.effect=="Use")){Place(m,grass[i]);stages[i]=2;}
 else if(stages[i]==2&&m.Grounded&&m.OnGrass&&m.ModuleStage>=m.stats._moduleSteps){Place(m,road[i]);stages[i]=3;}
 }
 }}
}}
