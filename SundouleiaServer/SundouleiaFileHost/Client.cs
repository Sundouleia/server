using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Sundouleia.FileHost.API.Grpc;

/**
  SundouleiaFileHost - A distributed file hosting service.
  Copyright (C) 2025 Sundouleia Authors

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License as
  published by the Free Software Foundation, either version 3 of the
  License, or (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU Affero General Public License for more details.

  You should have received a copy of the GNU Affero General Public License
  along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

namespace Sundouleia.FileHost.API;

// <summary>
// Client for interacting with Sundouleia File Host API. Intended to be injected as a dependency in Hostbuilder applications.
// </summary>
public class Client : IClient, IDisposable
{
	private readonly GrpcChannel _channel;
	private readonly FileHostAPI.FileHostAPIClient _client;

	// <summary>
	// Initializes a new instance of the Client class.
	// </summary>
	// <param name="baseAddress">The base address of the Sundouleia File Host server (e.g., "https://files.example.com:8080").</param>
	// <param name="psk">Pre-shared key for authenticating API requests.</param>
	public Client(string baseAddress, string psk)
	{
#if DEBUG
		AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
#endif
		_channel = GrpcChannel.ForAddress(baseAddress);
		_channel.Intercept(new AuthInterceptor(psk));
		_client = new FileHostAPI.FileHostAPIClient(_channel);
	}

	// <summary>
	// Gets temporary upload URLs for the specified file hashes. For hashes that already exist on the server, only download URLs will be returned.
	// </summary>
	// <param name="hashes">An array of file hashes for which to get upload and download URLs.</param>
	// <returns>A FileUrls object containing dictionaries of upload URLs for missing files and download URLs for existing files.</returns>
	public async Task<FileUrls> GetUploadUrlsAsync(IEnumerable<string> hashes)
	{
		var resp = await _client.AuthorizeUploadsAsync(new AuthorizeUploadsRequest
		{
			Hashes = { hashes }
		});
		return new FileUrls
		{
			UploadUrl = resp.UploadUrls.ToDictionary(kv => kv.Key, kv => kv.Value),
			DownloadUrl = resp.DownloadUrls.ToDictionary(kv => kv.Key, kv => kv.Value)
		};
	}

	// <summary>
	// Gets temporary upload URLs for the specified file hashes. For hashes that already exist on the server, only download URLs will be returned.
	// </summary>
	// <param name="hashes">An array of file hashes for which to get upload and download URLs.</param>
	// <returns>A FileUrls object containing dictionaries of upload URLs for missing files and download URLs for existing files.</returns>
	public FileUrls GetUploadUrls(IEnumerable<string> hashes)
	{
		var resp = _client.AuthorizeUploads(new AuthorizeUploadsRequest
		{
			Hashes = { hashes }
		});
		return new FileUrls
		{
			UploadUrl = resp.UploadUrls.ToDictionary(kv => kv.Key, kv => kv.Value),
			DownloadUrl = resp.DownloadUrls.ToDictionary(kv => kv.Key, kv => kv.Value)
		};
	}

	// <summary>
	// Gets temporary download URLs for the specified file hashes.
	// </summary>
	// <param name="hashes">An array of file hashes for which to get download URLs.</param>
	// <returns>A FileUrls object containing a dictionary of download URLs for the specified hashes.</returns>
	public async Task<FileUrls> GetDownloadUrlsAsync(IEnumerable<string> hashes)
	{
		var resp = await _client.AuthorizeDownloadsAsync(new AuthorizeDownloadsRequest
		{
			Hashes = { hashes }
		});
		return new FileUrls
		{
			DownloadUrl = resp.DownloadUrls.ToDictionary(kv => kv.Key, kv => kv.Value)
		};
	}

	// <summary>
	// Gets temporary download URLs for the specified file hashes.
	// </summary>
	// <param name="hashes">An array of file hashes for which to get download URLs.</param>
	// <returns>A FileUrls object containing a dictionary of download URLs for the specified hashes.</returns>
	public FileUrls GetDownloadUrls(IEnumerable<string> hashes)
	{
		var resp = _client.AuthorizeDownloads(new AuthorizeDownloadsRequest
		{
			Hashes = { hashes }
		});
		return new FileUrls
		{
			DownloadUrl = resp.DownloadUrls.ToDictionary(kv => kv.Key, kv => kv.Value)
		};
	}

	public void Dispose()
	{
		_channel.Dispose();
	}
}

/// <summary>
/// Interface for the Sundouleia File Host API client.
/// </summary>
public interface IClient
{
	Task<FileUrls> GetUploadUrlsAsync(IEnumerable<string> hashes);
	FileUrls GetUploadUrls(IEnumerable<string> hashes);
	Task<FileUrls> GetDownloadUrlsAsync(IEnumerable<string> hashes);
	FileUrls GetDownloadUrls(IEnumerable<string> hashes);
}
