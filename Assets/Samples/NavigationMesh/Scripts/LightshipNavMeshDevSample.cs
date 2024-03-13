// Copyright 2022-2024 Niantic.
using System.Collections;
using System.Collections.Generic;
// using Niantic.Lightship.AR.NavigationMesh;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System;

/// <summary>
/// This sample shows how to quickly used Niantic's NavMesh to add user driven point and click navigation
/// when you first touch the screen it will place your agent prefab
/// then if you tap again the agent will walk to that location
/// there is a toggle button to show hide the navigation mesh and path.
/// It assumes the _agentPrefab has LightshipNavMeshAgent on it.
/// You can overload it if you want to.
/// </summary>
public class LightshipNavMeshDevSample : MonoBehaviour
{
    [SerializeField]
    private Camera _camera;

    [FormerlySerializedAs("_gameboardManager")] [SerializeField]
    private LightshipNavMeshManager _navMeshManager;

    [FormerlySerializedAs("_agentPrefab")] [SerializeField]
    private GameObject agentPrefab;

    [FormerlySerializedAs("_Visualization")] [SerializeField]
    private GameObject visualization;

    private GameObject _creature;
    private LightshipNavMeshAgent _agent;

    private PlayerInput _lightshipInput;
    private InputAction _primaryTouch;


    [SerializeField] private OpenAISceneDescription openAISceneDescription;
    private string inputText = "Starting navigation assistance";
    private int speedSliderValue = 1;
    private TTSModel model = TTSModel.TTS_1;
    private TTSVoice voice = TTSVoice.Alloy;
    [SerializeField] private TTSManager ttsManager;
    

    private bool hasPlayedAudio = false;
    private bool hasInitialized = false;
    private bool screenshotTaken = false;

    private void Awake(){
        //Get the input actions.
        _lightshipInput = GetComponent<PlayerInput>();
        _primaryTouch = _lightshipInput.actions["Point"];
        
    }

    void Update()
    {
        // HandleTouch();

        // Play starting message only once at the begining
        if (!hasPlayedAudio)
        {
            Debug.Log("ttsManager");
            ttsManager.SynthesizeAndPlay(inputText, model, voice, speedSliderValue);
            hasPlayedAudio = true; // Set the flag so it doesn't play again.
        }

        // if (!screenshotTaken) 
        // {
        //     // CaptureAndAnalyzeImage();
        //     string sceneDescriptionText = "Start scene description: ";
        //     ttsManager.SynthesizeAndPlay(sceneDescriptionText, model, voice, speedSliderValue);
        //     StartCoroutine(CaptureAndAnalyzeImage());
        //     screenshotTaken = true; // Ensure this runs once
        // }
    }

    public void ToggleVisualisation()
    {
        if(_creature != null ){
            //turn off the rendering for the nav mesh
            _navMeshManager.GetComponent<LightshipNavMeshRenderer>().enabled =
                !_navMeshManager.GetComponent<LightshipNavMeshRenderer>().enabled;

            //turn off the path rendering on any agent
            _agent.GetComponent<LightshipNavMeshAgentPathRenderer>().enabled =
                !_agent.GetComponent<LightshipNavMeshAgentPathRenderer>().enabled;
        }
    }

    private void HandleTouch()
    {
        //Get the primaryTouch from our input actions.
        if (!_primaryTouch.WasPerformedThisFrame())
            return;
        else{
            //project the touch point from screen space into 3d and pass that to your agent as a destination
            Ray ray = _camera.ScreenPointToRay(_primaryTouch.ReadValue<Vector2>());
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit) &&
                _navMeshManager.LightshipNavMesh.IsOnNavMesh(hit.point, 0.2f) &&
                !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                if (_creature == null )
                {
                    //TODO: Add the is there enough space to place.
                    //have a nice fits/dont fit in the space.

                    _creature = Instantiate(agentPrefab);
                    _creature.transform.position = hit.point;
                    _agent = _creature.GetComponent<LightshipNavMeshAgent>();
                    visualization.SetActive(true);
                    

                }
                else
                {
                    _agent.SetDestination(hit.point);
                }
            }
        }
    }

    // RunCaptureAndAnalyzeImage is connected to the scene description button in the scene
    public void RunCaptureAndAnalyzeImage()
    {
        string sceneDescriptionText = "Start scene description: ";
        ttsManager.SynthesizeAndPlay(sceneDescriptionText, model, voice, speedSliderValue);
        StartCoroutine(CaptureAndAnalyzeImage());

    }
    
    // Capture the scene image and send to OpenAI GPT4-vision API for response
    public IEnumerator CaptureAndAnalyzeImage()
    {
        yield return new WaitForSeconds(1f);
        // Ensure the camera is not null
        if (_camera == null)
        {
            Debug.LogError("Camera is not assigned.");
            yield break;
        }

        // Set up the RenderTexture
        RenderTexture renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
        _camera.targetTexture = renderTexture;
        Texture2D screenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);

        // Render the camera's view
        _camera.Render();

        // Transfer image from RenderTexture to Texture2D
        RenderTexture.active = renderTexture;
        screenshot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        screenshot.Apply();

        // byte[] bytes = screenshot.EncodeToPNG();
        // Debug.Log("Screenshot captured!");
        // string fileName = SnapshotName();
        // // string fileName = "screenshot1.png";
        // System.IO.File.WriteAllBytes(fileName, bytes);

        // Clean up
        _camera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(renderTexture);

        // Convert the screenshot to Base64
        string base64Image = ConvertToBase64(screenshot);
        StartCoroutine(openAISceneDescription.SceneDescriptionBase64(base64Image, HandleDescriptionResult));

        Destroy(screenshot);
        
    }

    public static string ConvertToBase64(Texture2D texture)
    {
        byte[] imageBytes = texture.EncodeToJPG();
        return System.Convert.ToBase64String(imageBytes);
    }

    private string SnapshotName() 
    {
        int resWidth = Screen.width;
        int resHeight = Screen.height;
        
        return string.Format("{0}/snapshots/snap_{1}x{2}_{3}.png",
            Application.dataPath,
            resWidth,
            resHeight,
            System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
    }

    void HandleDescriptionResult(string result)
    {
        Debug.Log("Received description: " + result);
        string sceneDescriptionText = "Start scene description: " + result;
        ttsManager.SynthesizeAndPlay(result, model, voice, speedSliderValue);
    }

}
