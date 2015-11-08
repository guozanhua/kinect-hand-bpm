﻿using UnityEngine;
using SocketIO;
using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using Kinect = Windows.Kinect;

public class BodySourceView : MonoBehaviour 
{
    public Material BoneMaterial;
    public GameObject BodySourceManager;

    // really good design, and best we can do without writing some new classes
    private static Vector2 lastUniqueCoordinates;
    private static double firstEmissionTime = (DateTime.Now - new DateTime(1970, 1, 1)).TotalMilliseconds;
    private static double lastEmissionTime = 0;
    //private static long lastEmissionTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

    private SocketIOComponent socket;

    private int emitted = 0;
    private Dictionary<ulong, GameObject> _Bodies = new Dictionary<ulong, GameObject>();
    private BodySourceManager _BodyManager;

    private  double firstZ = 0;
    private  double lastX = Double.MaxValue;
    private  double lastY = Double.MaxValue;
    private  double lastTime = 0;
    private  double startBeat;
    private  double lastBeatTime;
    private  bool ignoreBeat = false;

    private  int[] bpms = { 0, 0, 0, 0, 0 };
    private  int beatcount = 0;

    private  int avgbpm(int bpm)
    {
        int sum = 0;
        for(int i = 0; i < bpms.Length; i++)
        {
            if(bpms[i] == 0)
            {
                return (int)((sum / i + bpm * 0.1) / 1.1);
            }
            sum += bpms[i];
        }
        return (int) ((sum / bpms.Length + bpm * 0.1) / 1.1);
    }
    
    private Dictionary<Kinect.JointType, Kinect.JointType> _BoneMap = new Dictionary<Kinect.JointType, Kinect.JointType>()
    {
        { Kinect.JointType.FootLeft, Kinect.JointType.AnkleLeft },
        { Kinect.JointType.AnkleLeft, Kinect.JointType.KneeLeft },
        { Kinect.JointType.KneeLeft, Kinect.JointType.HipLeft },
        { Kinect.JointType.HipLeft, Kinect.JointType.SpineBase },   

        { Kinect.JointType.FootRight, Kinect.JointType.AnkleRight },
        { Kinect.JointType.AnkleRight, Kinect.JointType.KneeRight },
        { Kinect.JointType.KneeRight, Kinect.JointType.HipRight },
        { Kinect.JointType.HipRight, Kinect.JointType.SpineBase },

        { Kinect.JointType.HandTipLeft, Kinect.JointType.HandLeft },
        { Kinect.JointType.ThumbLeft, Kinect.JointType.HandLeft },
        { Kinect.JointType.HandLeft, Kinect.JointType.WristLeft },
        { Kinect.JointType.WristLeft, Kinect.JointType.ElbowLeft },
        { Kinect.JointType.ElbowLeft, Kinect.JointType.ShoulderLeft },
        { Kinect.JointType.ShoulderLeft, Kinect.JointType.SpineShoulder },

        { Kinect.JointType.HandTipRight, Kinect.JointType.HandRight },
        { Kinect.JointType.ThumbRight, Kinect.JointType.HandRight },
        { Kinect.JointType.HandRight, Kinect.JointType.WristRight },
        { Kinect.JointType.WristRight, Kinect.JointType.ElbowRight },
        { Kinect.JointType.ElbowRight, Kinect.JointType.ShoulderRight },
        { Kinect.JointType.ShoulderRight, Kinect.JointType.SpineShoulder },

        { Kinect.JointType.SpineBase, Kinect.JointType.SpineMid },
        { Kinect.JointType.SpineMid, Kinect.JointType.SpineShoulder },
        { Kinect.JointType.SpineShoulder, Kinect.JointType.Neck },
        { Kinect.JointType.Neck, Kinect.JointType.Head },
    };
    
    void Update ()
    {
        if (BodySourceManager == null)
        {
            return;
        }
        if (socket == null)
        {
            print("Null socket");
            socket = BodySourceManager.AddComponent<SocketIOComponent>();
            //socket.Awake();
            socket.Start();
            socket.Emit("kinect",  JSONObject.StringObject("60"));
        }
        _BodyManager = BodySourceManager.GetComponent<BodySourceManager>();
        if (_BodyManager == null)
        {
            return;
        }
        
        Kinect.Body[] data = _BodyManager.GetData();
        if (data == null)
        {
            return;
        }
        
        List<ulong> trackedIds = new List<ulong>();
        foreach(var body in data)
        {
            if (body == null)
            {
                continue;
              }

            if (body.IsTracked)
            {
                trackedIds.Add (body.TrackingId);
            }
        }
        
        List<ulong> knownIds = new List<ulong>(_Bodies.Keys);
        
        // First delete untracked bodies
        //foreach(ulong trackingId in knownIds)
        //{
        //    if(!trackedIds.Contains(trackingId))
        //    {
        //        Destroy(_Bodies[trackingId]);
        //        _Bodies.Remove(trackingId);
        //    }
        //}

        foreach(var body in data)
        {
            if (body == null)
            {
                continue;
            }
            
            if(body.IsTracked)
            {
                if(!_Bodies.ContainsKey(body.TrackingId))
                {
                    _Bodies[body.TrackingId] = CreateBodyObject(body.TrackingId);
                }
                
                RefreshBodyObject(body, _Bodies[body.TrackingId]);
            }
        }
    }
    
    private GameObject CreateBodyObject(ulong id)
    {
        GameObject body = new GameObject("Body:" + id);
        
        for (Kinect.JointType jt = Kinect.JointType.SpineBase; jt <= Kinect.JointType.ThumbRight; jt++)
        {
            if (jt != Kinect.JointType.HandRight) continue;
            GameObject jointObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                
            LineRenderer lr = jointObj.AddComponent<LineRenderer>();
            lr.SetVertexCount(2);
            lr.material = BoneMaterial;
            lr.SetWidth(0.05f, 0.05f);
            
            jointObj.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
            jointObj.name = jt.ToString();
            jointObj.transform.parent = body.transform;
        }
        
        return body;
    }
    
    private void RefreshBodyObject(Kinect.Body body, GameObject bodyObject)
    {
        for (Kinect.JointType jt = Kinect.JointType.SpineBase; jt <= Kinect.JointType.ThumbRight; jt++)
        {
            //if (jt < Kinect.JointType.ShoulderLeft || jt > Kinect.JointType.HandRight || jt == Kinect.JointType.ShoulderLeft || jt == Kinect.JointType.ShoulderRight) continue;
            //if (jt < Kinect.JointType.ElbowRight || jt > Kinect.JointType.HandRight) continue;
            //if (jt != Kinect.JointType.HandRight && jt != Kinect.JointType.HandLeft) continue;
            if (jt != Kinect.JointType.HandRight) continue;
            Kinect.Joint sourceJoint = body.Joints[jt];
            Kinect.Joint? targetJoint = null;
            
            if(_BoneMap.ContainsKey(jt))
            {
                targetJoint = body.Joints[_BoneMap[jt]];
            }
            
            Transform jointObj = bodyObject.transform.FindChild(jt.ToString());
            jointObj.localPosition = GetVector3FromJoint(sourceJoint);
            
            LineRenderer lr = jointObj.GetComponent<LineRenderer>();
            if(targetJoint.HasValue)
            {
                Vector3 vectorPos = jointObj.localPosition;
                DateTime currentTime = DateTime.Now;
                double currentMillis = (currentTime - new DateTime(1970, 1, 1)).TotalMilliseconds;
                //long currentMillis = (long)(currentTime.Ticks / TimeSpan.TicksPerMillisecond);
                if ((System.Math.Abs(lastUniqueCoordinates.x - vectorPos.x) > 0.1 ||
                     System.Math.Abs(lastUniqueCoordinates.y - vectorPos.y) > 0.1) &&
                     currentMillis - lastEmissionTime > 50)
                     //&&
                     //currentmillis - lastemissiontime > 60)
                {
                    //String milliString = vectorPos[0] + " " + vectorPos[1] + " " + (currentMillis - firstEmissionTime).ToString() + " " + (++emitted);     // millisecond resolution
                    //String fullString = vectorPos.ToString() + " " + Convert.ToString(currentTime); // second-level resolution
                    //UnityEngine.Debug.Log("Reached emit loop, with value " + milliString);
                    handleBPM(vectorPos.x, vectorPos.y, vectorPos.z, currentMillis - firstEmissionTime, ++emitted);
                    //socket.Emit("kinect", JSONObject.StringObject(milliString));
                    //socket.Update();
                    lastUniqueCoordinates = vectorPos;
                    lastEmissionTime = currentMillis;
                }
                //socket.Update();
                lr.SetPosition(0, jointObj.localPosition);
                lr.SetPosition(1, GetVector3FromJoint(targetJoint.Value));
                lr.SetColors(GetColorForState (sourceJoint.TrackingState), GetColorForState(targetJoint.Value.TrackingState));
            }
            else
            {
                lr.enabled = false;
            }
        }
    }
    
    private static Color GetColorForState(Kinect.TrackingState state)
    {
        //switch (state)
        //{
        //case Kinect.TrackingState.Tracked:
        //    return Color.green;

        //case Kinect.TrackingState.Inferred:
            return Color.red;

        //default:
        //    return Color.black;
        //}
    }
    
    private static Vector3 GetVector3FromJoint(Kinect.Joint joint)
    {   
        return new Vector3(joint.Position.X * 10, joint.Position.Y * 10, joint.Position.Z * 10);
    }

    private  void handleBPM(double x, double y, double z, double t, int emitted)
    {
        if (firstZ == 0 && z > 0) firstZ = z;
        //print("Measurement " + emitted + ": x = " + x + ", y = " + y + ", z = " + z + ", t = " + t);
        if ((firstZ * 1.3 < z))
        {
            //print("Ignoring measurement of z = " + z);
            return;
        }
        double velocity = Math.Pow(Math.Pow(x - lastX, 2) + Math.Pow(y - lastY, 2), 0.5) / (t - lastTime);
        //print(t + " " + x + " " + y + " " + velocity);
        if (velocity < 0.004)
        {
            //print("Slow");
            if (!ignoreBeat)
            {
                startBeat = t;
                ignoreBeat = true;
            }
        } else if(velocity > 0.01)
        {
            //print("No longer slow");
            foundBeat(t);
            ignoreBeat = false;
        }
        lastX = x;
        lastY = y;
        lastTime = t;
    }

    private  void foundBeat(double t)
    {
        double avg = (t + startBeat) / 2;
        //print(avg + "    " + lastBeatTime);
        double bpm = 60000 / (avg - lastBeatTime);
        if (bpm > 200 || bpm < 72)
        {
            //print("Not actually " + bpm + " bpm");
            //print("Trace: ")
            lastBeatTime = avg;
            return;
        }
        if(lastBeatTime != 0)
        {
            print("Beat found: " + bpm + " BPM");
            bpms[beatcount % 5] =(int) bpm;
            beatcount++;
            print("Average bpm: " + avgbpm((int)bpm));
            socket.Emit("kinect", JSONObject.StringObject(""+avgbpm((int)bpm)));
        }
        lastBeatTime = avg;
    }
}
