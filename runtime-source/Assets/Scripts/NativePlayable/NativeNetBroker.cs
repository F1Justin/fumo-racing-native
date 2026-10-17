using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Security.Cryptography;
using UnityEngine;
namespace Fumo.NativePlayable
{
    // Pump owns all room state and JSON work on the Unity thread.
    public sealed class NativeNetBroker : IDisposable
    {
        sealed class Connection { public NativeNetTransport wire; public string id = Guid.NewGuid().ToString("N"), name; public Room room; public int seat; public bool ready, spectator, nextRace, watchesRooms, returned = true; public string resumeToken = Token(); public double accepted = Now, rejected = -1, detachedUntil, recoveryDeadline; public NativeNetMessage loaded, selection; public double rateStart = Now; public int rateCount; public Dictionary<string,int> sequences = new Dictionary<string,int>(); }
        sealed class Room { public string id; public int capacity, round; public bool racing, completed, setup; public string hostId, candidateTrack, config; public NativeNetMessage snapshot, results; public List<Connection> peers = new List<Connection>(); }
        const double GraceSeconds = 15;
        static double Now { get { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; } }
        static string Token() { var bytes = new byte[32]; using(var random = RandomNumberGenerator.Create()) random.GetBytes(bytes); return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        readonly Dictionary<string,Connection> sessions = new Dictionary<string,Connection>();
        TcpListener listener; volatile bool disposed; volatile string error;
        readonly Queue<TcpClient> pending = new Queue<TcpClient>();
        readonly List<Connection> connections = new List<Connection>();
        readonly Dictionary<string,Room> rooms = new Dictionary<string,Room>();
        public string Error { get { return error; } }
        public void Start(int port)
        {
            if (listener != null) throw new InvalidOperationException("Broker already started");
            try { listener = new TcpListener(IPAddress.IPv6Any, port); listener.Server.DualMode = true; listener.Start(32); }
            catch { try { if (listener != null) listener.Stop(); listener = new TcpListener(IPAddress.Any, port); listener.Start(32); } catch (Exception e) { error=e.Message; listener=null; return; } }
            new Thread(() => { while (!disposed) { try { var client = listener.AcceptTcpClient(); lock (pending) { if (pending.Count >= 32) client.Close(); else pending.Enqueue(client); } } catch (Exception e) { if (!disposed) error = e.Message; break; } } }) { IsBackground = true, Name = "Fumo broker accept" }.Start();
        }
        public void Pump()
        {
            lock (pending) { while (pending.Count > 0) { var accepted = pending.Dequeue(); if (connections.Count >= 512) accepted.Close(); else connections.Add(new Connection { wire = new NativeNetTransport(accepted) }); } }
            foreach(var c in sessions.Values.ToArray()) if(c.detachedUntil > 0 && Now >= c.detachedUntil) Remove(c);
            foreach (var c in connections.ToArray()) {
                if(!connections.Contains(c))continue;
                if(c.rejected>=0){if(Now-c.rejected>1)Detach(c);continue;}
                NativeNetMessage m; int budget = 128; while (budget-- > 0 && c.wire.TryReceive(out m)) { Handle(c,m); if (c.rejected >= 0 || !connections.Contains(c)) break; }
                if(!connections.Contains(c))continue;
                if(!c.wire.IsConnected){Detach(c);continue;}
                if(c.room==null && !c.watchesRooms && Now-c.accepted>15)Remove(c);
            }
            BroadcastRoomList();
        }
        string roomListSnapshot;
        string RoomListPayload() => JsonUtility.ToJson(new RoomList { rooms = rooms.Values.Select(r => new RoomInfo { id = r.id, name = r.peers.FirstOrDefault(p => p.id == r.hostId)?.name ?? "Player", count = r.peers.Count(p => !p.spectator), spectators = r.peers.Count(p => p.spectator), racing = r.racing, capacity = r.capacity }).ToArray() });
        void BroadcastRoomList()
        {
            var watchers = connections.Where(c => c.watchesRooms && c.room == null && c.rejected < 0 && c.wire.IsConnected).ToArray();
            if (watchers.Length == 0) { roomListSnapshot = null; return; }
            string payload = RoomListPayload(); if (payload == roomListSnapshot) return;
            roomListSnapshot = payload;
            foreach (var watcher in watchers) watcher.wire.Send(new NativeNetMessage { kind = "rooms", payload = payload });
        }
        void Reject(Connection c, string reason) { c.wire.Send(new NativeNetMessage { kind = "error", error = reason, payload = reason }); c.rejected = Now; }

        NativeNetPeer[] Peers(Room r) { return r.peers.Select(c => new NativeNetPeer { id=c.id,name=c.name,seat=c.seat,ready=c.ready,returned=c.returned,spectator=c.spectator,connected=c.wire!=null&&c.wire.IsConnected,host=c.id==r.hostId }).ToArray(); }
        void Broadcast(Room r, NativeNetMessage m) { m.room=r.id; m.round=r.round; foreach(var c in r.peers) if(c.wire != null) c.wire.Send(m); }
        void Lobby(Room r) { Broadcast(r,new NativeNetMessage {kind="lobby",capacity=r.capacity,peers=Peers(r),payload=r.config,setup=r.setup,candidateTrack=r.candidateTrack}); }
        void Reset(Room r, string kind, string reason) { r.racing=r.completed=r.setup=false; r.candidateTrack=null; r.round++; r.snapshot=r.results=null; foreach(var c in r.peers) {c.ready=false;c.returned=true;c.sequences.Clear();c.loaded=null;} Broadcast(r,new NativeNetMessage{kind=kind,error=reason,peers=Peers(r)}); Lobby(r); }
        void Handle(Connection c, NativeNetMessage m)
        {
            if (Now - c.rateStart >= 1) { c.rateStart=Now;c.rateCount=0; }
            if (++c.rateCount > 1000) { Reject(c,"Message rate exceeded");return; }
            if (m.version != NativeNetProtocol.Version || m.content != NativeNetProtocol.Content) { Reject(c,"Version mismatch");return; }
            if (m.kind=="resume" && c.room==null) { Resume(c,m);return; }
            if (m.kind == "list" || m.kind == "rooms-watch")
            {
                if (c.room != null) { Reject(c, "Room list requires a separate connection"); return; }
                c.watchesRooms = m.kind == "rooms-watch";
                c.wire.Send(new NativeNetMessage { kind = "rooms", payload = RoomListPayload() }); return;
            }
            if(c.room==null) {
                if(m.kind!="create"&&m.kind!="join") {Reject(c,"Handshake required");return;}
                if(!NativePlayerNamePolicy.TryNormalize(m.name ?? "Player",out var name,out var nameError)) {Reject(c,nameError);return;}
                Room r;
                if(m.kind=="create") { if(rooms.Count>=64){Reject(c,"Room limit reached");return;} string id=string.IsNullOrWhiteSpace(m.room)?Guid.NewGuid().ToString("N").Substring(0,8):m.room; if(id.Length>64||rooms.ContainsKey(id)){Reject(c,"Room unavailable");return;} r=new Room{id=id,hostId=c.id,capacity=m.capacity==0?8:m.capacity}; if(r.capacity<1||r.capacity>8){Reject(c,"Invalid capacity");return;} rooms.Add(id,r); }
                else { if(!rooms.TryGetValue(m.room??"",out r)){Reject(c,"Room missing");return;} if(!m.spectator && r.racing){Reject(c,"Race already started; join as spectator");return;} if(!m.spectator && r.completed && r.peers.Any(p=>!p.spectator&&!p.returned)){Reject(c,"Players are reviewing results");return;} if(m.spectator ? r.peers.Count(p=>p.spectator)>=32 : r.peers.Count(p=>!p.spectator)>=r.capacity){Reject(c,"Room full");return;} }
                c.room=r;c.name=name;c.spectator=m.spectator;c.seat=Enumerable.Range(c.spectator?8:0,c.spectator?32:r.capacity).First(s=>r.peers.All(p=>p.seat!=s));c.returned=!r.racing;r.peers.Add(c);
                sessions.Add(c.resumeToken,c);Welcome(c);Lobby(r);return;
            }
            Room room=c.room; bool host=c.id==room.hostId;
            if((!string.IsNullOrEmpty(m.player)&&m.player!=c.id)||(!string.IsNullOrEmpty(m.room)&&m.room!=room.id)||m.seat!=c.seat){Reject(c,"Sender identity mismatch");return;}
            c.recoveryDeadline=0;
            m.player=c.id;m.seat=c.seat;m.resumeToken=null;
            if(m.kind=="leave"){Remove(c);return;}
            if(m.kind=="ping"){c.wire.Send(new NativeNetMessage{kind="pong",room=room.id,round=room.round});return;}
            if(m.kind=="return-room")
            {
                if(m.round!=room.round || !room.completed || room.results==null)return;
                c.returned=true;c.ready=false;
                if(c.spectator&&c.nextRace)
                {
                    int available=Enumerable.Range(0,room.capacity).Where(seat=>room.peers.All(peer=>peer==c||peer.seat!=seat)).DefaultIfEmpty(-1).First();
                    if(available>=0){c.seat=available;c.spectator=false;c.nextRace=false;}
                }
                if(c.spectator && c.seat<8)c.seat=Enumerable.Range(8,32).Where(seat=>room.peers.All(peer=>peer==c||peer.seat!=seat)).DefaultIfEmpty(c.seat).First();
                c.wire.Send(new NativeNetMessage{kind="return-room",room=room.id,round=room.round,player=c.id,seat=c.seat,completed=true,returned=true,spectator=c.spectator,peers=Peers(room)});
                Lobby(room);return;
            }
            if(m.kind=="ready"&&!room.racing&&c.returned&&!c.spectator){c.ready=m.ready;Lobby(room);return;}
            if(m.kind=="selection"&&!room.racing&&c.returned&&!c.spectator){c.selection=m;Broadcast(room,m);return;}
            if(m.kind=="config"&&host&&!room.racing&&c.returned){room.config=m.payload;Broadcast(room,m);return;}
            if(m.kind=="start"&&host&&!room.racing&&room.peers.Count(p=>!p.spectator)>=2&&room.peers.Where(p=>!p.spectator).All(p=>p.returned&&p.ready&&p.wire!=null&&p.wire.IsConnected)){room.racing=true;room.completed=false;room.round++;room.snapshot=room.results=null;foreach(var p in room.peers){p.returned=false;p.sequences.Clear();p.loaded=null;}Broadcast(room,m);return;}
            if(m.kind=="setup" && host && !room.racing)
            {
                if(!room.setup && (room.peers.Count(p=>!p.spectator)<2 || !room.peers.Where(p=>!p.spectator).All(p=>p.ready&&p.returned&&p.wire!=null&&p.wire.IsConnected)))return;
                room.setup=true;Lobby(room);return;
            }
            if(m.kind=="candidate" && host && room.setup && !room.racing){room.candidateTrack=m.payload;Lobby(room);return;}
            if(m.kind=="role" && !room.racing && c.returned)
            {
                bool spectator=m.spectator;
                if(spectator==c.spectator)return;
                int first=spectator?8:0,count=spectator?32:room.capacity;
                int seat=Enumerable.Range(first,count).Where(s=>room.peers.All(p=>p==c||p.seat!=s)).DefaultIfEmpty(-1).First();
                if(seat<0){c.wire.Send(new NativeNetMessage{kind="notice",payload="Room full"});return;}
                c.spectator=spectator;c.seat=seat;c.ready=false;c.selection=null;
                c.wire.Send(new NativeNetMessage{kind="role",seat=seat,round=room.round,spectator=spectator,peers=Peers(room)});Lobby(room);return;
            }
            if(m.kind=="retire" && room.racing && !c.spectator)
            {c.spectator=true;c.ready=false;Broadcast(room,new NativeNetMessage{kind="retire",seat=c.seat,peers=Peers(room)});Lobby(room);return;}
            if(m.kind=="next-race" && c.spectator)
            {c.wire.Send(new NativeNetMessage{kind="notice",payload="Participation requested for next race"});c.nextRace=true;return;}
            if((m.kind=="abort"||m.kind=="rematch")&&host){Reset(room,m.kind,m.error);return;}
            bool game=m.kind=="input"||m.kind=="loaded"||m.kind=="snapshot"||m.kind=="events"||m.kind=="results";
            if((m.kind=="input"||m.kind=="loaded") && c.spectator)return;
            if(game && (m.round < room.round || (room.completed && m.round==room.round))) return;
            if(m.kind=="results" && host && room.racing && m.round==room.round)
            {
                var result=JsonUtility.FromJson<NativeNetworkSnapshot>(m.payload);
                if(result?.karts==null || result.karts.Length==0 || !result.karts.All(k=>k.finished)){Reject(c,"Race results incomplete");return;}
                room.racing=false;room.completed=true;room.setup=false;room.candidateTrack=null;room.results=m;room.snapshot=null;
                foreach(var peer in room.peers)peer.ready=false;
                Broadcast(room,m);Lobby(room);return;
            }
            if(game&&room.racing&&m.round==room.round&&((m.kind=="input"||m.kind=="loaded")||host)) { int old; if(m.sequence<0){Reject(c,"Invalid sequence");return;} if(c.sequences.TryGetValue(m.kind,out old)&&m.sequence<=old){return;} c.sequences[m.kind]=m.sequence;if(m.kind=="loaded")c.loaded=m;if(m.kind=="snapshot")room.snapshot=m;if(m.kind=="results")room.results=m;Broadcast(room,m);return; }
            Reject(c,"Message forbidden in current room state");
        }
        void Welcome(Connection c)
        {
            Room r=c.room;
            c.wire.Send(new NativeNetMessage{kind="welcome",room=r.id,player=c.id,seat=c.seat,round=r.round,capacity=r.capacity,ready=c.ready,racing=r.racing,completed=r.completed,returned=c.returned,spectator=c.spectator,host=c.id==r.hostId,setup=r.setup,candidateTrack=r.candidateTrack,resumeToken=c.resumeToken,peers=Peers(r),payload=r.config});
            if(c.id==r.hostId)foreach(var peer in r.peers){var cached=r.racing?peer.loaded:peer.selection;if(cached!=null){cached.room=r.id;cached.round=r.round;c.wire.Send(cached);}}
            if(r.snapshot!=null)c.wire.Send(r.snapshot);
            if(r.results!=null && !c.returned)c.wire.Send(r.results);
        }
        void Resume(Connection c, NativeNetMessage m)
        {
            Connection old;
            if(!sessions.TryGetValue(m.resumeToken??"",out old)||old.room==null){Reject(c,"Resume session unavailable");return;}
            if(old.detachedUntil>0 && Now>=old.detachedUntil){Remove(old);Reject(c,"Resume session expired");return;}
            if(m.room!=old.room.id||m.player!=old.id||m.seat!=old.seat){Reject(c,"Sender identity mismatch");return;}
            c.id=old.id;c.name=old.name;c.seat=old.seat;c.ready=old.ready;c.returned=old.returned;c.spectator=old.spectator;c.nextRace=old.nextRace;c.room=old.room;c.resumeToken=old.resumeToken;c.sequences=old.sequences;c.loaded=old.loaded;c.selection=old.selection;
            c.recoveryDeadline=old.recoveryDeadline>0?old.recoveryDeadline:old.detachedUntil;
            c.room.peers[c.room.peers.IndexOf(old)]=c;sessions[c.resumeToken]=c;
            // A displaced transport no longer owns the seat when its close is observed.
            old.room=null;connections.Remove(old);if(old.wire!=null)old.wire.Dispose();old.wire=null;
            Welcome(c);Lobby(c.room);
        }
        void Detach(Connection c)
        {
            connections.Remove(c);if(c.wire!=null)c.wire.Dispose();c.wire=null;
            if(c.room!=null){c.detachedUntil=c.recoveryDeadline>0?c.recoveryDeadline:Now+GraceSeconds;Lobby(c.room);}
        }
        void Remove(Connection c)
        {
            connections.Remove(c);if(c.wire!=null)c.wire.Dispose();c.wire=null;Room r=c.room;if(r==null)return;sessions.Remove(c.resumeToken);r.peers.Remove(c);c.room=null;
            if(c.id==r.hostId){Broadcast(r,new NativeNetMessage{kind="closed",error="Host disconnected"});rooms.Remove(r.id);foreach(var p in r.peers){sessions.Remove(p.resumeToken);p.room=null;p.rejected=Now;}r.peers.Clear();}
            else {if(r.racing && c.seat<8)Broadcast(r,new NativeNetMessage{kind="retire",seat=c.seat,peers=Peers(r)});Lobby(r);}
        }
        public void Dispose(){disposed=true;if(listener!=null)listener.Stop();lock(pending){while(pending.Count>0)pending.Dequeue().Close();}foreach(var c in connections)if(c.wire!=null){if(c.room!=null)c.wire.CloseGracefully(new NativeNetMessage{kind="closed",error="Host left"});else c.wire.Dispose();}connections.Clear();sessions.Clear();rooms.Clear();}
        [Serializable] public sealed class RoomInfo {public string id,name;public int count,capacity,spectators; public bool racing;}
        [Serializable] public sealed class RoomList {public RoomInfo[] rooms;}
    }
}
