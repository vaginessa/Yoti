﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System.Threading.Tasks;

using System.Threading;
using System.Text.RegularExpressions;
using Database;
using AudioProcessing.Recognizer;
using Windows.UI.Xaml.Media.Animation;

#if NETFX_CORE
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
#endif

#if __ANDROID__
using Android.Media;
using Android.Content.PM;
using Xamarin.Essentials;
#endif

#if __WASM__
using Uno.Foundation;
#endif

namespace BP.Shared.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {

		private Shared.AudioRecorder.Recorder recorder;
		private AudioProcessing.Recognizer.AudioRecognizer recognizer;
		private bool isRecording = false;
		private bool wasRecording = false;
		private byte[] uploadedSong;
		private byte[] recordedSong;
		private Database.Database database;
		private Dictionary<uint, List<ulong>> songValueDatabase;

		private Storyboard flickerAnimation;

		public MainPage()
        {
            this.InitializeComponent();

			recorder = new Shared.AudioRecorder.Recorder();
			database = new Database.Database();
			recognizer = new AudioRecognizer();
			songValueDatabase = database.GetSearchData();
			
			setupFlickerAnimation();
			
			UpdateSongList();
            textBlk.Text = "I am ready";
		}

		private void setupFlickerAnimation()
		{
			flickerAnimation = new Storyboard();
			//storyboard.Duration = new Duration(TimeSpan.FromSeconds(1.0));
			DoubleAnimation opacityAnimation = new DoubleAnimation()
			{
				From = 0.0,
				To = 1.0,
				BeginTime = TimeSpan.FromSeconds(1.0),
				AutoReverse = true,
				Duration = new Duration(TimeSpan.FromSeconds(0.18))
			};

			Storyboard.SetTarget(flickerAnimation, flickerIcon);
			Storyboard.SetTargetProperty(flickerAnimation, "Opacity");
			flickerAnimation.Children.Add(opacityAnimation);
			flickerAnimation.RepeatBehavior = RepeatBehavior.Forever;
		}

		private async void RecognizeBtn_Click(object sender, RoutedEventArgs e)
        {
			if (!isRecording)
			{
				flickerIcon.Visibility = Visibility.Visible;
				flickerAnimation.Begin();
				Task.Run(() => recorder.StartRecording());
				isRecording = true;
				textBlk.Text = "Called library and am recording...";

				await Task.Run(() => Thread.Sleep(10000));

				recorder.StopRecording();
				isRecording = false;
				wasRecording = true;
				textBlk.Text = "Stopped recording from lib.";
				flickerAnimation.Pause();
				flickerIcon.Visibility = Visibility.Collapsed;
				PlayBtn.Visibility = Visibility.Visible;
			} 
		}

		/// <summary>
		/// OBSOLETE
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private async void stopBtn_Click(object sender, RoutedEventArgs e)
        {
			if (isRecording)
			{
				recorder.StopRecording();
				isRecording = false;
				wasRecording = true;
				textBlk.Text = "Stopped recording from lib.";
			}
        }

        private async void playBtn_Click(object sender, RoutedEventArgs e)
        {
		#region UWP
#if NETFX_CORE
			if (await recorder.ReplayRecordingUWP(Dispatcher))
			{
				textBlk.Text = "Replaying recorded sound.";				
			}
#endif
			#endregion
		#region ANDROID
#if __ANDROID__
			if (wasRecording)
			{
				recorder.ReplayRecordingANDROID();
				textBlk.Text = "Replaying recorded sound.";
			}
#endif
		#endregion
		}

		private async void recognizeBtn_Click(object sender, RoutedEventArgs e)
		{
			AudioProcessing.AudioFormats.WavFormat recordedAudioWav;
			
			#region GETTING recordedAudioWav
#if !__WASM__
			recordedSong = await recorder.GetDataFromStream();
#endif
			#region UWP
#if NETFX_CORE
			recordedAudioWav = new AudioProcessing.AudioFormats.WavFormat(recordedSong);
#endif
			#endregion
			#region ANDROID
#if __ANDROID__

			//at android we only get raw data without metadata
			// so I have to convert them manually to shorts and then use different constructor
			short[] recordedDataShort = AudioProcessing.Tools.Converter.BytesToShorts(recordedSong);

			recordedAudioWav = new AudioProcessing.AudioFormats.WavFormat(
				Shared.AudioRecorder.Recorder.Parameters.SamplingRate,
				Shared.AudioRecorder.Recorder.Parameters.Channels,
				recordedDataShort.Length,
				recordedDataShort);
#endif
			#endregion
			#region WASM
#if __WASM__
			throw new NotImplementedException("Song recognition is not yet implemented in WASM");
#endif
			#endregion
			#endregion
			
			System.Diagnostics.Debug.WriteLine("[DEBUG] Channels: " + recordedAudioWav.Channels);
			System.Diagnostics.Debug.WriteLine("[DEBUG] SampleRate: " + recordedAudioWav.SampleRate);
			System.Diagnostics.Debug.WriteLine("[DEBUG] NumOfData: " + recordedAudioWav.NumOfDataSamples);
			System.Diagnostics.Debug.WriteLine("[DEBUG] ActualNumOfData: " + recordedAudioWav.Data.Length);

			uint? ID = recognizer.RecognizeSong(recordedAudioWav, songValueDatabase);

			textBlk.Text = $"ID of recognized song is {ID}";

		}

		private async void uploadNewSongBtn_Click(object sender, RoutedEventArgs e)
		{
			#region UWP
#if NETFX_CORE
			var picker = new Windows.Storage.Pickers.FileOpenPicker();
			picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
			picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
			picker.FileTypeFilter.Add(".wav");

			StorageFile file = await picker.PickSingleFileAsync();
			if (file != null)
			{
				var audioFileData = await file.OpenStreamForReadAsync();
				uploadedSong = new byte[(int)audioFileData.Length];
				audioFileData.Read(uploadedSong, 0, (int)audioFileData.Length);
				this.textBlk.Text = "Picked song: " + file.Name;
			}
			else
			{
				this.textBlk.Text = "Operation cancelled.";
			}

#endif
			#endregion
			#region ANDORID
#if __ANDROID__
			if (await getExternalStoragePermission())
			{
				PickOptions options = new PickOptions
				{
					PickerTitle = "Please select a wav song file",
					FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
					{
						{DevicePlatform.Android, new[]{"audio/x-wav"} }
					})
				};

				FileResult result = await FilePicker.PickAsync(options);

				if (result != null)
				{
					textBlk.Text = $"File selected: {result.FileName}";
					var audioFileData = await result.OpenReadAsync();
					uploadedSong = new byte[(int)audioFileData.Length];
					audioFileData.Read(uploadedSong, 0, (int)audioFileData.Length);
				}
				else
				{
					textBlk.Text = "No audio file selected";
				}

			}
			else
			{
				textBlk.Text = "Acces to read storage denied.";
			}
#endif
			#endregion
			#region WASM
#if __WASM__
			FileSelectedEvent -= OnNewSongUploadedEvent;
			FileSelectedEvent += OnNewSongUploadedEvent;
			WebAssemblyRuntime.InvokeJS(@"
				var input = document.createElement('input');
				input.type = 'file';
				input.accept = '.wav';
				input.onchange = e => {
					var file = e.target.files[0];
					//size in MBs cannot be bigger than 50
					if ((file.size / 1024 / 1024)>50){ 
						alert('File size exceeds 50 MB');
					}
					else
					{
						var reader = new FileReader();
						reader.readAsDataURL(file);
						reader.onload = readerEvent => {
							//this is the binary uploaded content
							var content = readerEvent.target.result; 
							//invoke C# method to get audio binary data
							var selectFile = Module.mono_bind_static_method(" + "\"[BP.Wasm] BP.MainPage:SelectFile\"" + @");
							selectFile(content);
						}
					};
				};
				input.click(); "
			);
#endif
			#endregion
		}

		/// <summary>
		/// OBSOLETE
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private async void addNewSongBtn_Click(object sender, RoutedEventArgs e)
		{
			/*
			if (uploadedSong != null && nameTxtBox.Text != "" && authorTxtBox.Text != "")
			{
				System.Diagnostics.Debug.WriteLine("[DEBUG] Adding new song into database.");
				var audioWav = new AudioProcessing.AudioFormats.WavFormat(uploadedSong);
				var tfps = recognizer.GetTimeFrequencyPoints(audioWav);
				uint songID = database.AddSong(nameTxtBox.Text, authorTxtBox.Text);
				database.AddFingerprint(tfps);
				System.Diagnostics.Debug.WriteLine($"[DEBUG] DS.Count BEFORE:{songValueDatabase.Count}");
				recognizer.AddTFPToDataStructure(tfps, songID, songValueDatabase);
				database.UpdateSearchData(songValueDatabase);
				System.Diagnostics.Debug.WriteLine($"[DEBUG] DS.Count AFTER :{songValueDatabase.Count}");
				UpdateSongList();

				textBlk.Text = "New song added to database.";
			}
			*/
		}

		#region WASM
#if __WASM__
		private async void uploadBtn_Click(object sender, RoutedEventArgs e)
		{
            FileSelectedEvent -=OnSongToRecognizeUploadedEvent;
            FileSelectedEvent += OnSongToRecognizeUploadedEvent;
            WebAssemblyRuntime.InvokeJS(@"
				console.log('calling javascript');
				var input = document.createElement('input');
				input.type = 'file';
				input.accept = '.wav';
				input.onchange = e => {
					var file = e.target.files[0];
					//size in MBs cannot be bigger than 5
					if ((file.size / 1024 / 1024)>5){ 
						alert('File size exceeds 5 MB');
					}
					else
					{
						var reader = new FileReader();
						reader.readAsDataURL(file);
						reader.onload = readerEvent => {
							//this is the binary uploaded content
							var content = readerEvent.target.result; 
							//invoke C# method to get audio binary data
							var selectFile = Module.mono_bind_static_method(" + "\"[BP.Wasm] BP.MainPage:SelectFile\""+@");
							selectFile(content);
						}
					};
				};
				input.click(); "
			);
		}
		public static void SelectFile(string fileAsDataUrl) => FileSelectedEvent?.Invoke(null, new FileSelectedEventHandlerArgs(fileAsDataUrl));

		private void OnSongToRecognizeUploadedEvent(object sender, FileSelectedEventHandlerArgs e)
		{
			FileSelectedEvent -= OnSongToRecognizeUploadedEvent;
			var base64Data = Regex.Match(e.FileAsDataUrl, @"data:audio/(?<type>.+?),(?<data>.+)").Groups["data"].Value;
			var binData = Convert.FromBase64String(base64Data); //this is the data I want
#if DEBUG
			for (int i = 0; i < 10; i++)
			{
				Console.Out.Write((char)binData[i]);
			}
			Console.Out.WriteLine();
#endif
			recordedSong = binData;
			Console.Out.WriteLine("Recognize song");
		}

		private void OnNewSongUploadedEvent(object sender, FileSelectedEventHandlerArgs e)
		{
			FileSelectedEvent -= OnNewSongUploadedEvent;
			var base64Data = Regex.Match(e.FileAsDataUrl, @"data:audio/(?<type>.+?),(?<data>.+)").Groups["data"].Value;
			uploadedSong = Convert.FromBase64String(base64Data); //this is the data I want
#if DEBUG
			for (int i = 0; i < 10; i++)
			{
				Console.Out.Write((char)uploadedSong[i]);
			}
			Console.Out.WriteLine();
#endif
			Console.Out.WriteLine("New song");
			//uploadedSong = binData;
		}

		private static event FileSelectedEventHandler FileSelectedEvent;

		private delegate void FileSelectedEventHandler(object sender, FileSelectedEventHandlerArgs args);

		private class FileSelectedEventHandlerArgs
		{
			public string FileAsDataUrl { get; }
			public FileSelectedEventHandlerArgs(string fileAsDataUrl) => FileAsDataUrl = fileAsDataUrl;

		}
#endif
		#endregion

		#region ANDROID - helper functions
#if __ANDROID__
		private async Task<bool> getExternalStoragePermission()
		{
			CancellationTokenSource source = new CancellationTokenSource();
			CancellationToken token = source.Token;
			return await Windows.Extensions.PermissionsHelper.TryGetPermission(token, Android.Manifest.Permission.ReadExternalStorage);
		}
#endif
#endregion
	
		private async void ListSongsBtn_Click(object sender, RoutedEventArgs e)
		{
			Frame.Navigate(typeof(SongList));
		}


		private void UpdateSongList()
		{
			var songs = database.GetSongs();
			List<string> songNames = new List<string>();
			foreach(Song song in songs)
			{
				songNames.Add(song.Name);
			}
			songList.ItemsSource = songNames;
		}


		// UI
		private async void ListSongs_Click(object sender, RoutedEventArgs e)
		{
			Frame.Navigate(typeof(SongList), database.GetSongs());
		}

		private async void AddNewSong_Click(object sender, RoutedEventArgs e)
		{
			ShowAddNewSongUI();
		}
		
		private async void CancelNewSong_Click(object sender, RoutedEventArgs e)
		{
			HideAddNewSongUI();
		}

		private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
		{
			ContentDialogResult result = await settingsContentDialog.ShowAsync();
		}


		#region UI HELPERS
		private void HideAddNewSongUI()
		{
			UploadGrid.Visibility = Visibility.Collapsed;
			AddNewSongBtn.Visibility = Visibility.Visible;
			ListSongsBtn.Visibility = Visibility.Visible;
		}

		private void ShowAddNewSongUI()
		{
			UploadGrid.Visibility = Visibility.Visible;
			AddNewSongBtn.Visibility = Visibility.Collapsed;
			ListSongsBtn.Visibility = Visibility.Collapsed;
		}
		#endregion
	}
}
