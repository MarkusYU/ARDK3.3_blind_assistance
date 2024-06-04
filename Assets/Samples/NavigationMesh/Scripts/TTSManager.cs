using UnityEngine;

/// <summary>
/// This class uses code from the TTSManager class of the OpenAI-Text-To-Speech-for-Unity project.
/// Available at: https://github.com/mapluisch/OpenAI-Text-To-Speech-for-Unity
/// <summary>
public class TTSManager : MonoBehaviour
{
    private OpenAIWrapper openAIWrapper;
    [SerializeField] private AudioPlayer audioPlayer;
    [SerializeField] private TTSModel model = TTSModel.TTS_1;
    [SerializeField] private TTSVoice voice = TTSVoice.Alloy;
    [SerializeField, Range(0.25f, 4.0f)] private float speed = 1f;
    
    private string lastSpokenText = string.Empty;
    private bool isSpeaking = false;

    private void OnEnable()
    {
        if (!openAIWrapper) this.openAIWrapper = FindObjectOfType<OpenAIWrapper>();
        if (!audioPlayer) this.audioPlayer = GetComponentInChildren<AudioPlayer>();
    }

    public async void SynthesizeAndPlay(string text)
    {
        if (isSpeaking || (lastSpokenText == "Start scene description: " && (text.StartsWith("Obstacle") || text.StartsWith("No"))))
        {
            Debug.LogWarning("Currently speaking or text starting with 'obstacle' or 'no' cannot be spoken after 'Start scene description: '.");
            return;
        }
        isSpeaking = true;

        audioPlayer.gameObject.SetActive(true);
        byte[] audioData = await openAIWrapper.RequestTextToSpeech(text, model, voice, speed);
        if (audioData != null)
        {
            audioPlayer.ProcessAudioBytes(audioData);
            lastSpokenText = text;
        }
        else
        {
            Debug.LogError("Failed to get audio data from OpenAI.");
        }

        isSpeaking = false;
    }

    public async void SynthesizeAndPlay(string text, TTSModel model, TTSVoice voice, float speed)
    {
        this.model = model;
        this.voice = voice;
        this.speed = speed;
        SynthesizeAndPlay(text);
    }
}