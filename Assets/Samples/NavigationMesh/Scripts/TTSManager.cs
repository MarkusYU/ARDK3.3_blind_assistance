using UnityEngine;

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

        isSpeaking = true; // Set the flag to true as speech starts

        audioPlayer.gameObject.SetActive(true);
        byte[] audioData = await openAIWrapper.RequestTextToSpeech(text, model, voice, speed);
        if (audioData != null)
        {
            // Debug.Log("Playing audio.");
            audioPlayer.ProcessAudioBytes(audioData);
            lastSpokenText = text;
            // Assume you have an event or a method to hook when the audio finishes playing
            // You should set isSpeaking back to false there. Example:
            // audioPlayer.OnAudioFinished += () => { isSpeaking = false; };
        }
        else
        {
            Debug.LogError("Failed to get audio data from OpenAI.");
        }

        isSpeaking = false; // Reset the flag when done, or in an event if the player supports it
    }

    public async void SynthesizeAndPlay(string text, TTSModel model, TTSVoice voice, float speed)
    {
        this.model = model;
        this.voice = voice;
        this.speed = speed;
        SynthesizeAndPlay(text);
    }
}