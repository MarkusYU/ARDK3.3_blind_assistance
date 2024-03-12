using UnityEngine;

public class TTSManager : MonoBehaviour
{
    private OpenAIWrapper openAIWrapper;
    [SerializeField] private AudioPlayer audioPlayer;
    [SerializeField] private TTSModel model = TTSModel.TTS_1;
    [SerializeField] private TTSVoice voice = TTSVoice.Alloy;
    [SerializeField, Range(0.25f, 4.0f)] private float speed = 1f;
    
    private string lastSpokenText = string.Empty;

    private void OnEnable()
    {
        if (!openAIWrapper) this.openAIWrapper = FindObjectOfType<OpenAIWrapper>();
        if (!audioPlayer) this.audioPlayer = GetComponentInChildren<AudioPlayer>();
    }

    public async void SynthesizeAndPlay(string text)
    {
        if (lastSpokenText == "Start scene description: " && (text.StartsWith("Obstacle") || text.StartsWith("No")))
        {
            Debug.LogWarning("Text starting with 'obstacle' or 'no' cannot be spoken after 'Start scene description: '.");
            return;
        }

        audioPlayer.gameObject.SetActive(true);
        byte[] audioData = await openAIWrapper.RequestTextToSpeech(text, model, voice, speed);
        if (audioData != null)
        {
            Debug.Log("Playing audio.");
            audioPlayer.ProcessAudioBytes(audioData);
            lastSpokenText = text;
        }
        else
        {
            Debug.LogError("Failed to get audio data from OpenAI.");
        }
    }

    public async void SynthesizeAndPlay(string text, TTSModel model, TTSVoice voice, float speed)
    {
        this.model = model;
        this.voice = voice;
        this.speed = speed;
        SynthesizeAndPlay(text);
    }
}