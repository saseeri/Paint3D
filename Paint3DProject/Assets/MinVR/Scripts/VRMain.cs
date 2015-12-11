﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace MinVR {

	/** VRMain must be the first script in Unity's execution order, something that must be set manually in the Unity Editor:
	 *  1. Go to Edit -> Project Settings -> Script Execution Order.
	 *  2. Click on the "+" button and select the MinVR.VRMain script.
	 *  3. Drag it above the bar labeled "Default Time".  This will set its order to "-100", which means its Update() method
	 *     will be called before the Update() method for any other script.
	 */
	 
	public class VRMain : MonoBehaviour {

		[Tooltip("Select this to connect to a MinVR Server to get tracker and other input events.  The server must already be running at the time your Unity program starts up.")]
		public bool connectToVRServer = true;
		[Tooltip("The IP address of the MinVR server (e.g., 127.0.0.1).")]
		public string VRServerIP = "localhost";
		[Tooltip("The port of the MinVR server (typically 3490).")]
		public int VRServerPort = 3490;


		// Built-in support for faking input from a set of 6-DOF trackers
		[Tooltip("To make debugging VR apps easier, you can use the mouse and keyboard to create 'fake' input for two trackers. " +
		         "Press the '1' or '2' key to switch between controlling tracker1 or tracker2.  Move the mouse around the screen " +
		         "to move the 3D position of that tracker within a plane parallel to the screen.  Hold down 'left shift' while " +
		         "moving the mouse vertically to change the 3D depth.  Hold 'x', 'y', or 'z' while moving the mouse horizontally " +
		         "to rotate the tracker around the X, Y, or Z axis.  This is only for use during debugging on your laptop.  Make " +
		         "sure to disable 'Fake Trackers for Debugging' when you deploy your app!")]
		public bool fakeTrackersForDebugging = false;

		[Tooltip("The name of the VREvent generated by the first fake tracker.")]
		public string fakeTracker1Event = "Brush_Move";
		private Vector3 tracker1Pos = new Vector3();
		private Quaternion tracker1Rot = Quaternion.identity;

		[Tooltip("The name of the VREvent generated by the second fake tracker.")]
		public string fakeTracker2Event = "Hand_Move";
		private Vector3 tracker2Pos = new Vector3();
		private Quaternion tracker2Rot = Quaternion.identity;

		[Tooltip("Fake head tracking with arrow keys. 'up' moves forward, 'down' moves backward, 'left' rotates left, 'right' rotates right.")]
		public bool fakeHeadTracking = false;
		public string fakeHeadTrackerEvent = "Head_Move";
		private Vector3 headTrackerPos = new Vector3(0, 1, -10); // the default camera position in Unity
		private Quaternion headTrackerRot = Quaternion.identity;

		private int curTracker = 0;
		private float lastx = -9999;
		private float lasty = -9999;


		
		public delegate void VREventDelegate(VREvent e);
		public static event VREventDelegate VREventHandler;
		
		private VRNetClient _netClient;
		
		// When Unity starts up, Update seems to be called twice before we reach the EndOfFrame callback, so we maintain
		// a state variable here to make sure that we don't request events twice before requesting swapbuffers.
		private enum NetState {PreUpdateNext, PostRenderNext}
		private NetState _state = NetState.PreUpdateNext;



		void Start () {
			if (connectToVRServer) {
				_netClient = new VRNetClient(VRServerIP, VRServerPort);
			}
		}

		// See important note above... this Update() method MUST be called before any others in your Unity App.
		void Update () {
			if (_state == NetState.PreUpdateNext) {
				// Since we force this Script to be the first one that Unity calls, this gives us a hook to create
				// something like a "PreUpdate()" function.  It would have been nice if Unity provided this for us,
				// but they do not (yet) provide a PreUpdate() callback.
				PreUpdate ();

				// We also need a callback when the scene is done rendering, so we request that callback each frame.
				StartCoroutine(EndOfFrameCallback());
			}
		}

		IEnumerator EndOfFrameCallback() {
			// This is a fancy feature of Unity and C# and is the only way I know how to get a callback after Unity
			// has finished completely rendering the frame, which may include rendering more than one camera.
			// The yield command pauses execution of this function until the EndOfFrame is reached.
			yield return new WaitForEndOfFrame();

			if (_state == NetState.PostRenderNext) {
				PostRender();				
			}
		}


		// AT THE START OF EACH FRAME: SYNCHRONIZE INPUT EVENTS AND CALL ONVREVENT CALLBACK FUNCTIONS
		void PreUpdate() {
			List<VREvent> inputEvents = new List<VREvent> ();

			// TODO: Add input events generated in Unity to the list of inputEvents to sync them across all clients


			// ----- FAKE TRACKER INPUT FOR DEBUGGING IN DESKTOP MODE USING MOUSE INPUT -----
			if (fakeTrackersForDebugging) {
				float x = Input.mousePosition.x;
				float y = Input.mousePosition.y;
				// first time through
				if (lastx == -9999) {
					lastx = x;
					lasty = y;
					return;
				}
					
				if (Input.GetKeyDown ("1")) {
					curTracker = 0;
				} else if (Input.GetKeyDown ("2")) {
					curTracker = 1;
				}

				if (Input.GetKey ("x")) {
					float angle = 0.1f * (x - lastx);
					if (curTracker == 0) {
						tracker1Rot = Quaternion.AngleAxis (angle, new Vector3 (1f, 0f, 0f)) * tracker1Rot;
					} else if (curTracker == 1) {
						tracker2Rot = Quaternion.AngleAxis (angle, new Vector3 (1f, 0f, 0f)) * tracker2Rot;
					}
				} else if (Input.GetKey ("y")) {
					float angle = 0.1f * (x - lastx);
					if (curTracker == 0) {
						tracker1Rot = Quaternion.AngleAxis (angle, new Vector3 (0f, 1f, 0f)) * tracker1Rot;
					} else if (curTracker == 1) {
						tracker2Rot = Quaternion.AngleAxis (angle, new Vector3 (0f, 1f, 0f)) * tracker2Rot;
					}
				} else if (Input.GetKey ("z")) {
					float angle = 0.1f * (x - lastx);
					if (curTracker == 0) {
						tracker1Rot = Quaternion.AngleAxis (angle, new Vector3 (0f, 0f, 1f)) * tracker1Rot;
					} else if (curTracker == 1) {
						tracker2Rot = Quaternion.AngleAxis (angle, new Vector3 (0f, 0f, 1f)) * tracker2Rot;
					}
				} else if (Input.GetKey ("left shift")) {
					float depth = 0.005f * (y - lasty);
					if (curTracker == 0) {
						tracker1Pos += depth * Camera.main.transform.forward;
					} else if (curTracker == 1) {
						tracker2Pos += depth * Camera.main.transform.forward;
					}
				} else {
					Ray ray = Camera.main.ScreenPointToRay (new Vector3 (x, y, 0f));
					Plane p = new Plane ();
					float dist = 0.0f;
					if (curTracker == 0) {
						p.SetNormalAndPosition (-Camera.main.transform.forward, tracker1Pos);
						if (p.Raycast (ray, out dist)) {
							tracker1Pos = ray.GetPoint (dist);
						}
					} else if (curTracker == 1) {
						p.SetNormalAndPosition (-Camera.main.transform.forward, tracker2Pos);
						if (p.Raycast (ray, out dist)) {
							tracker2Pos = ray.GetPoint (dist);
						}
					}
				}

				Matrix4x4 m1 = Matrix4x4.TRS (tracker1Pos, tracker1Rot, Vector3.one);
				double[] d1 = VRConvert.ToDoubleArray (m1);
				VRDataIndex data1 = new VRDataIndex ();
				data1.AddData ("Transform", d1);
				inputEvents.Add (new VREvent (fakeTracker1Event, data1));
				Matrix4x4 m2 = Matrix4x4.TRS (tracker2Pos, tracker2Rot, Vector3.one);
				double[] d2 = VRConvert.ToDoubleArray (m2);
				VRDataIndex data2 = new VRDataIndex ();
				data2.AddData ("Transform", d2);
				inputEvents.Add (new VREvent (fakeTracker2Event, data2));
				lastx = x;
				lasty = y;
			}
			if (fakeHeadTracking) {
				if (Input.GetKey("up")) {
					headTrackerPos += 0.1f * Camera.main.transform.forward;
				}
				else if (Input.GetKey("down")) {
					headTrackerPos -= 0.1f * Camera.main.transform.forward;
				}
				else if (Input.GetKey("left")) {
					headTrackerRot *= Quaternion.AngleAxis (-1.0f, new Vector3 (0f, 1f, 0f));
				}
				else if (Input.GetKey("right")) {
					headTrackerRot *= Quaternion.AngleAxis (1.0f, new Vector3 (0f, 1f, 0f));
				}
				Matrix4x4 m3 = Matrix4x4.TRS (headTrackerPos, headTrackerRot, Vector3.one);
				double[] d3 = VRConvert.ToDoubleArray (m3);
				VRDataIndex data3 = new VRDataIndex ();
				data3.AddData ("Transform", d3);
				inputEvents.Add (new VREvent (fakeHeadTrackerEvent, data3));
			}
			// ----- END FAKE TRACKER INPUT -----


			// Synchronize with the server
			if (_netClient != null) {
				_netClient.SynchronizeInputEventsAcrossAllNodes(ref inputEvents);
			}

			// Call any event callback functions that have been registered with the VREventHandler
			for (int i=0; i<inputEvents.Count; i++) {
				if (VREventHandler != null) {
					VREventHandler(inputEvents[i]);
				}
			}		
			_state = NetState.PostRenderNext;
		}



		// AT END END OF EACH FRAME:  WAIT FOR THE SIGNAL THAT ALL CLIENTS ARE ALSO READY, THEN SWAPBUFFERS
		void PostRender() {
			if (_netClient != null) {
				_netClient.SynchronizeSwapBuffersAcrossAllNodes ();
			}
			_state = NetState.PreUpdateNext;
		}

	}

} // namespace MinVR
