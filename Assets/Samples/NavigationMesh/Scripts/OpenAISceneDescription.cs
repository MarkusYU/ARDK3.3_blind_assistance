using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System;

/// <summary>
/// This class uses the example code from the OpenAI GPT-4 API webpage.
/// Available at: https://platform.openai.com/docs/guides/vision
/// </summary>
public class OpenAISceneDescription : MonoBehaviour
{
    private string openAIURL = "https://api.openai.com/v1/";
    private string apiKey = "";


    public void GetDescriptionText()
    {
        Debug.Log("Example image");
        StartCoroutine(SceneDescriptionWithURL("https://upload.wikimedia.org/wikipedia/commons/thumb/d/dd/Gfp-wisconsin-madison-the-nature-boardwalk.jpg/2560px-Gfp-wisconsin-madison-the-nature-boardwalk.jpg"));
    }

    public IEnumerator SceneDescriptionWithURL(string imageUrl)
    {
        // Construct the JSON payload
        string jsonPayload = "{\"model\":\"gpt-4-vision-preview\",\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"What’s in this image? using no more than 5 words\"},{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + imageUrl + "\"}}]}],\"max_tokens\":300}";
        
        using (UnityWebRequest www = new UnityWebRequest(openAIURL + "chat/completions", "POST"))
        {
            // Convert the payload to a byte array
            byte[] bodyRaw = new System.Text.UTF8Encoding().GetBytes(jsonPayload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + apiKey);

            // Send the request
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(www.error);
            }
            else
            {
                // Handle the response
                string responseText = www.downloadHandler.text;
                string searchString = "\"content\": \"";
                int startIndex = responseText.IndexOf(searchString) + searchString.Length;
                int endIndex = responseText.IndexOf("\"", startIndex);
                string content = responseText.Substring(startIndex, endIndex - startIndex);
            }
        }
    }

    public IEnumerator SceneDescriptionBase64(string base64Image, Action<string> callback)
    {
        // Construct the JSON payload
        string jsonPayload = $"{{\"model\":\"gpt-4-vision-preview\",\"messages\":[{{\"role\":\"user\",\"content\":[{{\"type\":\"text\",\"text\":\"Can you tell me is there a walkable sidewalk of more than 10m (not road for cars) in this direction? Can you also tell me what is displayed on this image? Please using no more than 20 words in total and do not include any punctuation beyond full stops and commas in the answer. Start with there is/is no walkable sidewalk in this direction.\"}},{{\"type\":\"image_url\",\"image_url\":\"data:image/jpeg;base64,{base64Image}\"}}]}}],\"max_tokens\":300}}";
        
        using (UnityWebRequest www = new UnityWebRequest(openAIURL + "chat/completions", "POST"))
        {
            // Convert the payload to a byte array
            byte[] bodyRaw = new System.Text.UTF8Encoding().GetBytes(jsonPayload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + apiKey);

            // Send the request
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(www.error);
            }
            else
            {
                // Handle the response
                string responseText = www.downloadHandler.text;
                string searchString = "\"content\": \"";
                int startIndex = responseText.IndexOf(searchString) + searchString.Length;
                int endIndex = responseText.IndexOf("\"", startIndex);
                string content = responseText.Substring(startIndex, endIndex - startIndex);

                callback?.Invoke(content);
                
            }
        }
    }

}