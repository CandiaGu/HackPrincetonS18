/******************************************************************************\
* Copyright (C) Leap Motion, Inc. 2011-2014.                                   *
* Leap Motion proprietary. Licensed under Apache 2.0                           *
* Available at http://www.apache.org/licenses/LICENSE-2.0.html                 *
\******************************************************************************/

using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Leap;
using System;
using UnityEngine.Networking;
using System.Collections;
using System.Text.RegularExpressions;
 




/**
* The Controller object that instantiates hands and tools to represent the hands and tools tracked
* by the Leap Motion device.
*
* HandController is a Unity MonoBehavior instance that serves as the interface between your Unity application
* and the Leap Motion service.
*
* The HandController script is attached to the HandController prefab. Drop a HandController prefab 
* into a scene to add 3D, motion-controlled hands. The hands are placed above the prefab at their 
* real-world relationship to the physical Leap device. You can change the transform of the prefab to 
* adjust the orientation and the size of the hands in the scene. You can change the 
* HandController.handMovementScale property to change the range
* of motion of the hands without changing the apparent model size.
*
* When the HandController is active in a scene, it adds the specified 3D models for the hands to the
* scene whenever physical hands are tracked by the Leap Motion hardware. By default, these objects are
* destroyed when the physical hands are lost and recreated when tracking resumes. The asset package
* provides a variety of hands that you can use in conjunction with the hand controller. 
*/
public class HandController : MonoBehaviour {

  // Reference distance from thumb base to pinky base in mm.
  protected const float GIZMO_SCALE = 5.0f;
  /** Conversion factor for millimeters to meters. */
  protected const float MM_TO_M = 0.001f;

  /** Whether to use a separate model for left and right hands (true); or mirror the same model for both hands (false). */ 
  public bool separateLeftRight = false;
  /** The GameObject containing graphics to use for the left hand or both hands if separateLeftRight is false. */
  public HandModel leftGraphicsModel;
  /** The GameObject containing colliders to use for the left hand or both hands if separateLeftRight is false. */
  public HandModel leftPhysicsModel;
  /** The graphics hand model to use for the right hand. */
  public HandModel rightGraphicsModel;
  /** The physics hand model to use for the right hand. */
  public HandModel rightPhysicsModel;
  // If this is null hands will have no parent
  public Transform handParent = null;
  private Transform rotateAround;
  /** The GameObject containing both graphics and colliders for tools. */
  public ToolModel toolModel;

  public int dim = 2;

  /** Set true if the Leap Motion hardware is mounted on an HMD; otherwise, leave false. */
  public bool isHeadMounted = false;
  /** Reverses the z axis. */
  public bool mirrorZAxis = false;

  /** If hands are in charge of Destroying themselves, make this false. */
  public bool destroyHands = true;

  /** The scale factors for hand movement. Set greater than 1 to give the hands a greater range of motion. */
  public Vector3 handMovementScale = new Vector3(1, 0, 0);

	public float curAngle = 0;
  // Recording parameters.
  /** Set true to enable recording. */
  public bool enableRecordPlayback = false;
  /** The file to record or playback from. */
  public TextAsset recordingAsset;
  /** Playback speed. Set to 1.0 for normal speed. */
  public float recorderSpeed = 1.0f;
  /** Whether to loop the playback. */
  public bool recorderLoop = true;
  
	private GameObject cameraObj;
  /** The object used to control recording and playback.*/
  protected LeapRecorder recorder_ = new LeapRecorder();
  
  /** The underlying Leap Motion Controller object.*/
  protected Controller leap_controller_;

  /** The list of all hand graphic objects owned by this HandController.*/
  protected Dictionary<int, HandModel> hand_graphics_;
  /** The list of all hand physics objects owned by this HandController.*/
  protected Dictionary<int, HandModel> hand_physics_;
  /** The list of all tool objects owned by this HandController.*/
  protected Dictionary<int, ToolModel> tools_;

  private bool flag_initialized_ = false;
  private long prev_graphics_id_ = 0;
  private long prev_physics_id_ = 0;

  private int numPoint = 0;

  public LineRenderer lineRenderer;

  public Color c1 = Color.yellow;
  public Color c2 = Color.red;

  public List<Vector3> linePoints;
	public List<Vector3> equaPoints;

	public string equaIMG;
	public GameObject cube;
	private Texture2D tex;

	Material m_Material;

	//var this:transform;
  
  /** Draws the Leap Motion gizmo when in the Unity editor. */
  void OnDrawGizmos() {
    // Draws the little Leap Motion Controller in the Editor view.
    Gizmos.matrix = Matrix4x4.Scale(GIZMO_SCALE * Vector3.one);
    Gizmos.DrawIcon(transform.position, "leap_motion.png");
  }

  /** 
  * Initializes the Leap Motion policy flags.
  * The POLICY_OPTIMIZE_HMD flag improves tracking for head-mounted devices.
  */
  void InitializeFlags()
  {
    // Optimize for top-down tracking if on head mounted display.
    Controller.PolicyFlag policy_flags = leap_controller_.PolicyFlags;
    if (isHeadMounted)
      policy_flags |= Controller.PolicyFlag.POLICY_OPTIMIZE_HMD;
    else
      policy_flags &= ~Controller.PolicyFlag.POLICY_OPTIMIZE_HMD;

    leap_controller_.SetPolicyFlags(policy_flags);
  }

  /** Creates a new Leap Controller object. */
  void Awake() {
    leap_controller_ = new Controller();
  }

  /** Initalizes the hand and tool lists and recording, if enabled.*/
  void Start() {


	//GetText ();
	StartCoroutine(GetText());
	//lineRenderer = gameObject.AddComponent<LineRenderer> ();
	
	//LINE RENDERER
	lineRenderer = gameObject.AddComponent<LineRenderer> ();
	linePoints = new List<Vector3> ();
	//lineRenderer.positionCount = 1;
	lineRenderer.material = new Material (Shader.Find ("Particles/Additive"));
		lineRenderer.startWidth = 15;
		lineRenderer.endWidth = 15;
	float alpha = 1.0f;
	Gradient gradient = new Gradient();
	gradient.SetKeys(
		new GradientColorKey[] { new GradientColorKey(c1, 0.0f), new GradientColorKey(c2, 1.0f) },
		new GradientAlphaKey[] { new GradientAlphaKey(alpha, 0.0f), new GradientAlphaKey(alpha, 1.0f) }
	);
	lineRenderer.colorGradient = gradient;
	lineRenderer.loop = false;

	//lineRenderer.SetPosition(0, new Vector3(5,5,100));


    // Initialize hand lookup tables.
    hand_graphics_ = new Dictionary<int, HandModel>();
    hand_physics_ = new Dictionary<int, HandModel>();

    tools_ = new Dictionary<int, ToolModel>();

    if (leap_controller_ == null) {
      Debug.LogWarning(
          "Cannot connect to controller. Make sure you have Leap Motion v2.0+ installed");
    }

    if (enableRecordPlayback && recordingAsset != null)
      recorder_.Load(recordingAsset);
  }

  /**
  * Turns off collisions between the specified GameObject and all hands.
  * Subject to the limitations of Unity Physics.IgnoreCollisions(). 
  * See http://docs.unity3d.com/ScriptReference/Physics.IgnoreCollision.html.
  */
  public void IgnoreCollisionsWithHands(GameObject to_ignore, bool ignore = true) {
    foreach (HandModel hand in hand_physics_.Values)
      Leap.Utils.IgnoreCollisions(hand.gameObject, to_ignore, ignore);
  }

  /** Creates a new HandModel instance. */
  protected HandModel CreateHand(HandModel model) {
    HandModel hand_model = Instantiate(model, transform.position, transform.rotation)
                           as HandModel;
    hand_model.gameObject.SetActive(true);
    Leap.Utils.IgnoreCollisions(hand_model.gameObject, gameObject);
    if (handParent != null) {
      hand_model.transform.SetParent(handParent.transform);
    }
    return hand_model;
  }

  /** 
  * Destroys a HandModel instance if HandController.destroyHands is true (the default).
  * If you set destroyHands to false, you must destroy the hand instances elsewhere in your code.
  */
  protected void DestroyHand(HandModel hand_model) {
    if (destroyHands)
      Destroy(hand_model.gameObject);
    else
      hand_model.SetLeapHand(null);
  }

  /** 
  * Updates hands based on tracking data in the specified Leap HandList object.
  * Active HandModel instances are updated if the hand they represent is still
  * present in the Leap HandList; otherwise, the HandModel is removed. If new
  * Leap Hand objects are present in the Leap HandList, new HandModels are 
  * created and added to the HandController hand list. 
  * @param all_hands The dictionary containing the HandModels to update.
  * @param leap_hands The list of hands from the a Leap Frame instance.
  * @param left_model The HandModel instance to use for new left hands.
  * @param right_model The HandModel instance to use for new right hands.
  */
	protected void UpdateHandModels(Dictionary<int, HandModel> all_hands,
                                  HandList leap_hands,
                                  HandModel left_model, HandModel right_model) {
		List<int> ids_to_check = new List<int> (all_hands.Keys);

		// Go through all the active hands and update them.
		int num_hands = leap_hands.Count;
		for (int h = 0; h < num_hands; ++h) {
			Hand leap_hand = leap_hands [h];
      
			//Get the finger_tip location if finger is pinched
			if (leap_hand.PinchStrength > 0.2) {
				if (linePoints.Count == 0) {
					lineRenderer.SetVertexCount (0);
				}

				FingerList point = leap_hand.Fingers.FingerType (Finger.FingerType.TYPE_INDEX);
				Vector3 new_tip = UnityVectorExtension.ToUnity (point [0].TipPosition);
				new_tip.x = new_tip.x * 1.5f;



				lineRenderer.SetPosition (lineRenderer.positionCount - 1, new_tip);

				lineRenderer.positionCount = lineRenderer.positionCount + 1;
				lineRenderer.SetPosition (lineRenderer.positionCount - 1, new_tip);
				linePoints.Add (new_tip);
				Debug.Log ("pinching: " + linePoints.Count);
				//Debug.Log (linePoints [linePoints.Count - 1]);
				if (numPoint % 15 == 0) {
					lineRenderer.Simplify (.1f);
				}
				numPoint++;

			} else {
				
				//transform.Translate(Vector3.up * Time.deltaTime*50);
				
				if (linePoints.Count > 0) {
					Debug.Log ("not pinching: " + convertVectorListToString (linePoints) + linePoints.Count);
					StartCoroutine (GetText ());
					//lineRenderer = gameObject.AddComponent<LineRenderer> ();
					//lineRenderer.positionCount = 100;

					tex = new Texture2D (128, 128);

					string fromBase64 = equaIMG;
					byte[] data = System.Convert.FromBase64String (fromBase64);
					tex.LoadImage (data);


					cube = GameObject.Find ("Cube");
					cube.transform.position = new Vector3 (0, -100, 0);
					cube.transform.localScale = new Vector3 (tex.width * 2, tex.height * 2, 10);
					//cube.transform.Rotate(new Vector3(0, 0, 180));

					m_Material = cube.GetComponent<Renderer> ().material;
					m_Material.mainTexture = tex;


					linePoints.Clear ();
					lineRenderer.SetVertexCount (0);

					//lineRenderer = GetComponent<LineRenderer>();
					lineRenderer.positionCount = equaPoints.Count;
					lineRenderer.SetPositions (equaPoints.ToArray ());
				} else {



				}
				int b = checkSwipe (leap_hand);
				if (b != 0) {
					curAngle = b * 30 * Time.deltaTime;
					cameraObj = GameObject.Find ("Main Camera");
					rotateAround = GameObject.Find ("HandController").GetComponent<Transform> ();

					cameraObj.transform.LookAt (rotateAround.position);
					cameraObj.transform.RotateAround (rotateAround.position, Vector3.up, curAngle);//.5f);
					//lineRenderer.transform.RotateAround(Vector3.zero, Vector3.up, b*30 * Time.deltaTime);

				}



				HandModel model = (mirrorZAxis != leap_hand.IsLeft) ? left_model : right_model;

				// If we've mirrored since this hand was updated, destroy it.
				if (all_hands.ContainsKey (leap_hand.Id) &&
				      all_hands [leap_hand.Id].IsMirrored () != mirrorZAxis) {
					DestroyHand (all_hands [leap_hand.Id]);
					all_hands.Remove (leap_hand.Id);
				}

				// Only create or update if the hand is enabled.
				if (model != null) {
					ids_to_check.Remove (leap_hand.Id);
					// Create the hand and initialized it if it doesn't exist yet.
					if (!all_hands.ContainsKey (leap_hand.Id)) {
						HandModel new_hand = CreateHand (model);
						new_hand.SetLeapHand (leap_hand);
						new_hand.MirrorZAxis (mirrorZAxis);
						new_hand.SetController (this);

						// Set scaling based on reference hand.
						float hand_scale = MM_TO_M * leap_hand.PalmWidth / new_hand.handModelPalmWidth;
						new_hand.transform.localScale = hand_scale * transform.lossyScale;
						//new_hand.transform.RotateAround(Vector3.zero, Vector3.up, -curAngle);
						new_hand.InitHand (); 
		
						new_hand.UpdateHand ();
						all_hands [leap_hand.Id] = new_hand;
					} else {
						// Make sure we update the Leap Hand reference.
						HandModel hand_model = all_hands [leap_hand.Id];
						hand_model.SetLeapHand (leap_hand);
						hand_model.MirrorZAxis (mirrorZAxis);

						// Set scaling based on reference hand.
						float hand_scale = MM_TO_M * leap_hand.PalmWidth / hand_model.handModelPalmWidth;
						hand_model.transform.localScale = hand_scale * transform.lossyScale;
						hand_model.UpdateHand ();
					}
				}
			}

			// Destroy all hands with defunct IDs.
			for (int i = 0; i < ids_to_check.Count; ++i) {
				DestroyHand (all_hands [ids_to_check [i]]);
				all_hands.Remove (ids_to_check [i]);
			}
		}
	}

  /** Creates a ToolModel instance. */
  protected ToolModel CreateTool(ToolModel model) {
    ToolModel tool_model = Instantiate(model, transform.position, transform.rotation) as ToolModel;
    tool_model.gameObject.SetActive(true);
    Leap.Utils.IgnoreCollisions(tool_model.gameObject, gameObject);
    return tool_model;
  }

  /** 
  * Updates tools based on tracking data in the specified Leap ToolList object.
  * Active ToolModel instances are updated if the tool they represent is still
  * present in the Leap ToolList; otherwise, the ToolModel is removed. If new
  * Leap Tool objects are present in the Leap ToolList, new ToolModels are 
  * created and added to the HandController tool list. 
  * @param all_tools The dictionary containing the ToolModels to update.
  * @param leap_tools The list of tools from the a Leap Frame instance.
  * @param model The ToolModel instance to use for new tools.
  */
  protected void UpdateToolModels(Dictionary<int, ToolModel> all_tools,
                                  ToolList leap_tools, ToolModel model) {
    List<int> ids_to_check = new List<int>(all_tools.Keys);

    // Go through all the active tools and update them.
    int num_tools = leap_tools.Count;
    for (int h = 0; h < num_tools; ++h) {
      Tool leap_tool = leap_tools[h];
      
      // Only create or update if the tool is enabled.
      if (model) {

        ids_to_check.Remove(leap_tool.Id);

        // Create the tool and initialized it if it doesn't exist yet.
        if (!all_tools.ContainsKey(leap_tool.Id)) {
          ToolModel new_tool = CreateTool(model);
          new_tool.SetController(this);
          new_tool.SetLeapTool(leap_tool);
          new_tool.InitTool();
          all_tools[leap_tool.Id] = new_tool;
        }

        // Make sure we update the Leap Tool reference.
        ToolModel tool_model = all_tools[leap_tool.Id];
        tool_model.SetLeapTool(leap_tool);
        tool_model.MirrorZAxis(mirrorZAxis);

        // Set scaling.
        tool_model.transform.localScale = transform.lossyScale;

        tool_model.UpdateTool();
      }
    }

    // Destroy all tools with defunct IDs.
    for (int i = 0; i < ids_to_check.Count; ++i) {
      Destroy(all_tools[ids_to_check[i]].gameObject);
      all_tools.Remove(ids_to_check[i]);
    }
  }

  /** Returns the Leap Controller instance. */
  public Controller GetLeapController() {
    return leap_controller_;
  }

  /**
  * Returns the latest frame object.
  *
  * If the recorder object is playing a recording, then the frame is taken from the recording.
  * Otherwise, the frame comes from the Leap Motion Controller itself.
  */
  public Frame GetFrame() {
    if (enableRecordPlayback && recorder_.state == RecorderState.Playing)
      return recorder_.GetCurrentFrame();

    return leap_controller_.Frame();
  }

  /** Updates the graphics objects. */
  void Update() {

	//transform.RotateAround(Vector3.zero, Vector3.up, 30 * Time.deltaTime);

    if (leap_controller_ == null)
      return;
    
    UpdateRecorder();
    Frame frame = GetFrame();

    if (frame != null && !flag_initialized_)
    {
      InitializeFlags();
    }
    if (frame.Id != prev_graphics_id_)
    {
      UpdateHandModels(hand_graphics_, frame.Hands, leftGraphicsModel, rightGraphicsModel);
      prev_graphics_id_ = frame.Id;
    }
  }

  /** Updates the physics objects */
  void FixedUpdate() {


		
    if (leap_controller_ == null)
      return;

    Frame frame = GetFrame();

    if (frame.Id != prev_physics_id_)
    {
      UpdateHandModels(hand_physics_, frame.Hands, leftPhysicsModel, rightPhysicsModel);
      UpdateToolModels(tools_, frame.Tools, toolModel);
      prev_physics_id_ = frame.Id;
    }
  }

  /** True, if the Leap Motion hardware is plugged in and this application is connected to the Leap Motion service. */
  public bool IsConnected() {
    return leap_controller_.IsConnected;
  }

  /** Returns information describing the device hardware. */
  public LeapDeviceInfo GetDeviceInfo() {
    LeapDeviceInfo info = new LeapDeviceInfo(LeapDeviceType.Peripheral);
    DeviceList devices = leap_controller_.Devices;
    if (devices.Count != 1) {
      return info;
    }
    // TODO: Add baseline & offset when included in API
    // NOTE: Alternative is to use device type since all parameters are invariant
    info.isEmbedded = devices [0].IsEmbedded;
    info.horizontalViewAngle = devices[0].HorizontalViewAngle * Mathf.Rad2Deg;
    info.verticalViewAngle = devices[0].VerticalViewAngle * Mathf.Rad2Deg;
    info.trackingRange = devices[0].Range / 1000f;
    info.serialID = devices[0].SerialNumber;
    return info;
  }

  /** Returns a copy of the hand model list. */
  public HandModel[] GetAllGraphicsHands() {
    if (hand_graphics_ == null)
      return new HandModel[0];

    HandModel[] models = new HandModel[hand_graphics_.Count];
    hand_graphics_.Values.CopyTo(models, 0);
    return models;
  }

  /** Returns a copy of the physics model list. */
  public HandModel[] GetAllPhysicsHands() {
    if (hand_physics_ == null)
      return new HandModel[0];

    HandModel[] models = new HandModel[hand_physics_.Count];
    hand_physics_.Values.CopyTo(models, 0);
    return models;
  }

  /** Destroys all hands owned by this HandController instance. */
  public void DestroyAllHands() {
    if (hand_graphics_ != null) {
      foreach (HandModel model in hand_graphics_.Values)
        Destroy(model.gameObject);

      hand_graphics_.Clear();
    }
    if (hand_physics_ != null) {
      foreach (HandModel model in hand_physics_.Values)
        Destroy(model.gameObject);

      hand_physics_.Clear();
    }
  }
  
  /** The current frame position divided by the total number of frames in the recording. */
  public float GetRecordingProgress() {
    return recorder_.GetProgress();
  }

  /** Stops recording or playback and resets the frame counter to the beginning. */
  public void StopRecording() {
    recorder_.Stop();
  }

  /** Start getting frames from the LeapRecorder object rather than the Leap service. */
  public void PlayRecording() {
    recorder_.Play();
  }

  /** Stops playback or recording without resetting the frame counter. */
  public void PauseRecording() {
    recorder_.Pause();
  }

  /** 
  * Saves the current recording to a new file, returns the path, and starts playback.
  * @return string The path to the saved recording.
  */
  public string FinishAndSaveRecording() {
    string path = recorder_.SaveToNewFile();
    recorder_.Play();
    return path;
  }

  /** Discards any frames recorded so far. */
  public void ResetRecording() {
    recorder_.Reset();
  }

  /** Starts saving frames. */
  public void Record() {
    recorder_.Record();
  }

  /** Called in Update() to send frames to the recorder. */
  protected void UpdateRecorder() {
    if (!enableRecordPlayback)
      return;

    recorder_.speed = recorderSpeed;
    recorder_.loop = recorderLoop;

    if (recorder_.state == RecorderState.Recording) {
      recorder_.AddFrame(leap_controller_.Frame());
    }
    else {
      recorder_.NextFrame();
    }
  }

	IEnumerator GetText() {


		UnityWebRequest www = UnityWebRequest.Put("https://www.wolframcloud.com/objects/lucypictures01/junk/demo", convertVectorListToString(linePoints));
		//if(dim==3)
		//UnityWebRequest	www = UnityWebRequest.Put("https://www.wolframcloud.com/objects/lucypictures01/junk/demo1", convertVectorListToString(linePoints));

		www.SetRequestHeader("Content-Type", "application/json");

//		UnityEngine.WWW CreateUnityWebRequestV6(string url, string param) {
//			Dictionary<string, string> headers = new Dictionary<string, string>();
//			headers.Add("Content-Type", "application/json");
//			byte[] body = Encoding.UTF8.GetBytes([);
//			UnityEngine.WWW www = new UnityEngine.WWW(url, body, headers);
//			return www;
//		}

		// execute the request
		//var content2 = response.Content;
		//Debug.Log (content);


		//
//		form.AddField ("undefined","[[2,3],[3,4],[5,6]]");
//		form.AddField ("Body","[[2,3],[3,4],[5,6]]");
//		UnityWebRequest www = UnityWebRequest.Post("https://www.wolframcloud.com/objects/adhebbar/junk/demo",body);
//
//
//
		yield return www.SendWebRequest();

		if(www.isNetworkError || www.isHttpError) {
			Debug.Log(www.error);
		}
		else {
			// Show results as text
			string res = www.downloadHandler.text;
			Debug.Log ("og" + convertVectorListToString (linePoints));
			Debug.Log("res" + res);

			// Or retrieve results as binary data
			byte[] results = www.downloadHandler.data;
			convertStringToVectorList (res);

		}
	}

	string convertVectorListToString(List<Vector3> list){
		string res = "[";
		foreach( Vector3 t in list){
			string temp = (t.x).ToString() + "," + (t.y).ToString() + "," + (t.z).ToString();
			res += "[" + temp + "]" + ",";
		}
		res = res.Substring(0, res.Length-1) + "]";

		return res;

	}

	void convertStringToVectorList(string str){
		AllData data = new AllData (); 
		data = JsonUtility.FromJson<AllData>(str);

		string temp = str.Substring(0,str.IndexOf ("Image"));
		MatchCollection allNums = Regex.Matches (temp, @"\-*\d+");//.Cast<Match>().Select(m => m.Value).ToArray();//\-*\D+");

		int count = 0; int x=0; int y=0; int z = 0;
		List<Vector3> allVectors = new List<Vector3>();
		foreach (Match m in allNums) {

//			if( dim == 2){
				if (count % 2 == 0) {
					x = Int32.Parse (m.Value);
					//
				} else {
					if (count % 2 == 1) {
						y = Int32.Parse (m.Value);
						allVectors.Add (new Vector3 (x, y, 0));
						Debug.Log ("data: " + x + " " + y + " " + 0);
					}
				}
				count++;
//			}


//			//if( dim == 3){
//				if (count % 3 == 0) {
//					x = Int32.Parse (m.Value);
//					//
//				} else {
//					if (count % 3 == 1) {
//						y = Int32.Parse (m.Value);
//					} else {
//						z = Int32.Parse (m.Value);
//						allVectors.Add (new Vector3 (x, y, z));
//						Debug.Log ("data: " + x + " " + y + " " + z);
//					}
//				}
//				count++;
//			//}
				
		}
		equaPoints = allVectors;
		Debug.Log ("data:" + allNums[0]);

		//equaPoints = new Vector3[data.Data.Length];

		//convert int
//		foreach (Vector2 elem in data.Data) {
//			
//			equaPoints[count] =  new Vector3 (elem.x, 0, elem.y);
//			count++;
//
//		}

		equaIMG = data.Image;

	}

	[Serializable]
	public class AllData
	{
		public Vector2[] Data;
		public String Image;
	}

	/*
returns -1 left; 0 nothing; 1 right
*/
	/* checks for swipe gesture */
		int checkSwipe(Hand hand){
		//Checking how many are extended and the angle
		float countAngle = 0;

		//Goes through each finger and checks how many of the extended fingers are facing the same way
		foreach (Pointable p in hand.Pointables){ 
			Finger finger = new Finger(p);
			if(finger.IsExtended)
			{
				
				Vector pointingToward = p.Direction;
				countAngle = countAngle + pointingToward.Yaw;
			}
		}
		if(countAngle >= 5){
			return 1;
		}

		if(countAngle <= -5)
		{

			return -1;
		}
		return 0;
	}
		

}


