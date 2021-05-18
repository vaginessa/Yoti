﻿using Yoti.Server.Models;
using SharedTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AudioRecognitionLibrary.AudioFormats;
using AudioRecognitionLibrary;
using AudioRecognitionLibrary.Recognizer;
using System.IO;

namespace Yoti.Server.Controllers
{
	[Route("[controller]")]
	[ApiController]
	public class RecognitionController : ControllerBase
	{
		public RecognitionController(SongContext context, SearchDataSingleton searchDataCollection, ILogger<RecognitionController> logger)
		{
			_context = context;
			_searchDataInstance = searchDataCollection;
			_logger = logger;
		}

		#region private fields

		/// <summary>
		/// Database context.
		/// </summary>
		private readonly SongContext _context;

		/// <summary>
		/// In memory search data.
		/// </summary>
		private readonly SearchDataSingleton _searchDataInstance;

		/// <summary>
		/// Logger.
		/// </summary>
		private readonly ILogger _logger;

		/// <summary>
		/// Recognizer algorithm .
		/// </summary>
		private readonly AudioRecognizer _recognizer = new AudioRecognizer();
		#endregion


		/// <summary>
		/// POST: recognition/AddNewSong
		/// </summary>
		/// <param name="songToUpload">Preprocessed song to be added into the database.</param>
		/// <returns>Uploaded song.</returns>
		#region Upload new song
		[HttpPost("[action]")]
		public ActionResult<Song> AddNewSong(PreprocessedSongData songToUpload)
		{
			Song newSong = new Song { Author = songToUpload.Author, Name = songToUpload.Name, Lyrics = songToUpload.Lyrics, BPM = songToUpload.BPM };

			_logger.LogInformation("Getting correct searchdata");
			Dictionary<uint, List<ulong>> searchData = GetSearchDataByBPM(songToUpload.BPM);

			// Save song metadata
			_context.Songs.Add(newSong);
			_context.SaveChanges();
			uint maxId = _context.Songs.Max(song => song.Id);

			_logger.LogInformation("Addding TFPs to database");
			_recognizer.AddFingerprintToDatabase(songToUpload.Fingerprint, maxId, searchData);

			// Update data in database
			_searchDataInstance.SaveToDB(songToUpload.BPM);
			_context.SaveChanges();

			return CreatedAtAction(nameof(GetSong), new { id = newSong.Id }, newSong);
		}
		#endregion

		/// <summary>
		/// POST: recognition/RecognizeSong
		/// </summary>
		/// <param name="songToUpload">Preprocessed audio to be recognized.</param>
		/// <returns>Wrapped recognized song with info about recognition process.</returns>
		#region Recognize song
		[HttpPost("[action]")]
		public async Task<ActionResult<RecognitionResult>> RecognizeSong(PreprocessedSongData songToUpload)
		{
			var stringWriter = new StringWriter();

			_logger.LogDebug("Getting correct searchdata");
			Dictionary<uint, List<ulong>> searchData = GetSearchDataByBPM(songToUpload.BPM);

			_logger.LogDebug("Recognizing song");
			double maxProbability = 0;
			uint? songId = _recognizer.RecognizeSong(songToUpload.Fingerprint, searchData, out maxProbability, songToUpload.TFPCount, stringWriter);

			if (songId == null)
			{
				_logger.LogDebug("Song not found by BPM");
				foreach(KeyValuePair<int, Dictionary<uint, List<ulong>>> entry in _searchDataInstance.SearchData)
				{
					if (entry.Key == songToUpload.BPM)
						continue; //skip searchdata that was already searched through


					searchData = GetSearchDataByBPM(entry.Key);
					uint? potentialSongId = _recognizer.RecognizeSong(songToUpload.Fingerprint, searchData, out double probability, songToUpload.TFPCount, stringWriter);

					// If result is not null and probabilty is higher than current max
					// remember the id and new max probability
					if (potentialSongId != null && probability > maxProbability)
					{
						_logger.LogDebug($"New potential song id found: {potentialSongId} with proba: {probability} in BPM: {entry.Key}");
						songId = potentialSongId;
						maxProbability = probability;
					}
				}
			}
			// Write result probability
			if (songId != null)
				await stringWriter.WriteLineAsync($"Recognized song with ID: {songId} is a {Math.Min(100d, maxProbability):##.#}% match.");

			stringWriter.Close();

			if (songId == null)
			{
				return new RecognitionResult
				{
					Song = null,
					DetailInfo = stringWriter.ToString()
				};
			}
			else
			{
				return new RecognitionResult
				{
					Song = await _context.Songs.FindAsync((uint)songId),
					DetailInfo = stringWriter.ToString()
				};

			}
		}
		#endregion


		/// <summary>
		/// DELETE: recognition/DeleteSong
		/// </summary>
		/// <param name="song">Song to delete.</param>
		/// <returns>Deleted song.</returns>
		#region Delete test
		[HttpDelete("[action]")]
		public async Task<ActionResult<Song>> DeleteSong(Song song)
		{
			
			if (! await _context.Songs.ContainsAsync(song))
			{
				return NotFound();
			}

			_context.Songs.Remove(song);
			DeleteSongFromSearchData(song);
			_context.SaveChanges();

			return song;
		}
		#endregion


		/// <summary>
		/// GET: recognition/getsongs
		/// </summary>
		/// <returns>List of songs in the database.</returns>
		#region Get all songs
		[HttpGet("[action]")]
		public async Task<ActionResult<List<Song>>> GetSongs()
		{
			return await _context.Songs.ToListAsync();
		}
		#endregion


		/// <summary>
		/// GET: recognition/getsong/{id}
		/// </summary>
		/// <param name="id">Id of the song to return.</param>
		/// <returns>Song with corresponding Id.</returns>
		#region Get song by Id
		[HttpGet("[action]/{id}")]
		public async Task<ActionResult<Song>> GetSong(uint id)
		{
			var song = await _context.Songs.FindAsync(id);
			if (song == null)
			{
				return NotFound();
			}

			return song;
		}
		#endregion


		/// <summary>
		/// DELETE: recognition/deletesongbyid/{id}
		/// </summary>
		/// <param name="id">Id of the song to be deleted.</param>
		/// <returns>Deleted song.</returns>
		#region Delete test by Id
		[HttpDelete("[action]/{id}")]
		public async Task<ActionResult<Song>> DeleteSongById(uint id)
		{
			var song = await _context.Songs.FindAsync(id);
			if (song == null)
			{
				return NotFound();
			}

			if (!await _context.Songs.ContainsAsync(song))
			{
				return NotFound();
			}

			_context.Songs.Remove(song);
			DeleteSongFromSearchData(song);
			_context.SaveChanges();

			return song;
		}
		#endregion

		#region Private helpers

		/// <summary>
		/// Obtain search data from database by BPM.
		/// </summary>
		/// <param name="BPM">BPM determining search data to be returned.</param>
		/// <returns>Search data of songs with given BPM.</returns>
		private Dictionary<uint, List<ulong>> GetSearchDataByBPM(int BPM)
		{
			if (!_searchDataInstance.SearchData.ContainsKey(BPM)) //doesnt contains the BPM yet -> add it
			{
				_searchDataInstance.SearchData.TryAdd(
					BPM, //BPM
					new Dictionary<uint, List<ulong>>()); //empty SongData
			}
			return _searchDataInstance.SearchData[BPM];
		}

		/// <summary>
		/// Set search data by given BPM.
		/// </summary>
		/// <param name="BPM">BPM of the search data</param>
		/// <param name="searchData">Search data to be set</param>
		private void SetSearchDataByBPM(int BPM, Dictionary<uint, List<ulong>> searchData)
		{
			// It doesnt contains the BPM yet -> add it
			if (!_searchDataInstance.SearchData.ContainsKey(BPM)) 
			{
				_searchDataInstance.SearchData.TryAdd(
					BPM, //BPM
					searchData); //empty SongData
			}
			// Replace current search data on the BPM
			else
			{
				_searchDataInstance.SearchData[BPM] = searchData;
			}
			
		}

		/// <summary>
		/// Delete search data of given song from database.
		/// </summary>
		/// <param name="song">Song whose search data are to be deleted.</param>
		private void DeleteSongFromSearchData(Song song)
		{
			uint deleteSongId= song.Id;

			Dictionary<uint, List<ulong>> oldSearchData = GetSearchDataByBPM(song.BPM);
			Dictionary<uint, List<ulong>> newSearchData = new Dictionary<uint, List<ulong>>();

			// Iterate over all entries in old search data
			foreach (KeyValuePair<uint, List<ulong>> entry in oldSearchData)
			{
				List<ulong> songDataList = new List<ulong>();

				// Iterate over all songDatas (Abs data & songID).
				foreach (ulong songData in entry.Value)
				{
					// If deleteSongId is different from the Id in current songData
					// add it to new songDataList that will be in new search data.
					// Cast songData int is because ulong songID consists of:
					// 32 bits of Absolute time of Anchor
					// 32 bits of songID
					if (deleteSongId != (uint)songData)
					{
						// Add songData to new search Data
						songDataList.Add(songData);
					}
				}

				// If some songData survive on entry.Key 
				// put them into newSearchData
				if (songDataList.Count != 0)
				{
					newSearchData.Add(entry.Key, songDataList);
				}
			}

			// Replace new search Data
			_searchDataInstance.SearchData[song.BPM] = newSearchData;
			_searchDataInstance.SaveToDB(song.BPM);

		}

		#endregion
	}
}
