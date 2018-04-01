using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class cameraScript : MonoBehaviour {

	private Transform rotateAround;

	// Use this for initialization
	void Start () {
		rotateAround = GameObject.Find ("HandController").GetComponent<Transform> ();

	}

	// Update is called once per frame
	void Update () {
		transform.LookAt(rotateAround.position);
		transform.RotateAround(rotateAround.position, Vector3.up, 5);
		//transform.Translate(Vector3.up * Time.deltaTime*50);
	}
}
