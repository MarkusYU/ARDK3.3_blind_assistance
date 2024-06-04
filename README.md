# NavigateAssistant Project for the Visually Impaired
This project is built on top of the Lightship ARDK 3.3 Sample project.

## __Quick links:__
* [ARDK API Reference](https://lightship.dev/docs/ardk/apiref/Niantic/)
* [Documentation](https://lightship.dev/docs/ardk/sample_projects/)
* [ARDK Sample Project](https://github.com/niantic-lightship/ardk-samples)

# Recording Datasets for Playback
AR Playback is a new, powerful feature for in-editor testing. The Recording sample will allow you to create your own dataset of an area near you. For more information, see [the Playback documentation](https://lightship.dev/docs/ardk/features/playback/).

You can also try out the feature with a pre-recorded dataset. Two are available as extra assets in our release:

* [Gandhi Statue](https://github.com/niantic-lightship/ardk-samples/releases/download/3.1.0/GandhiStatue_PlaybackDataset.tgz)

* [Relic Statue](https://github.com/niantic-lightship/ardk-samples/releases/download/3.1.0/Relic_PlaybackDataset.tgz)


After downloading a dataset, extract it to a folder of your choice, then open the Unity Editor to configure your Lightship settings:

1. Open Project Settings from the Edit menu, then scroll down to XR Plugin Management and select Niantic Lightship SDK.
2. Enable Editor Playback, then input the absolute path to your dataset in the Dataset Path field.

# Package Dependencies
These packages are included in the sample project:

[ardk-upm](https://github.com/niantic-lightship/ardk-upm)

[sharedar-upm](https://github.com/niantic-lightship/sharedar-upm)

[Vector Graphics](com.unity.vectorgraphics)
