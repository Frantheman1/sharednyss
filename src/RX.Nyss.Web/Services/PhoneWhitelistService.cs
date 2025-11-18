using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Azure;
using RX.Nyss.Common.Utils;
using RX.Nyss.Common.Utils.Logging;
using RX.Nyss.Web.Configuration;

namespace RX.Nyss.Web.Services
{
	public interface IPhoneWhitelistService
	{
		Task AddPhoneNumbers(IEnumerable<string> phoneNumbers);
	}

	public class PhoneWhitelistService : IPhoneWhitelistService
	{
		private readonly BlobProvider _blobProvider;
		private readonly INyssWebConfig _config;
		private readonly ILoggerAdapter _logger;

		public PhoneWhitelistService(INyssWebConfig config, ILoggerAdapter loggerAdapter, IAzureClientFactory<BlobServiceClient> azureClientFactory)
		{
			_config = config;
			_logger = loggerAdapter;
			var blobServiceClient = azureClientFactory.CreateClient("SmsGatewayBlobClient");
			_blobProvider = new BlobProvider(blobServiceClient, config.SmsGatewayBlobContainerName);
		}

		public async Task AddPhoneNumbers(IEnumerable<string> phoneNumbers)
		{
			if (phoneNumbers == null)
			{
				return;
			}

			var normalized = phoneNumbers
				.Where(n => !string.IsNullOrWhiteSpace(n))
				.Select(n => n.Trim())
				.Where(n => n.Length > 0)
				.Distinct(StringComparer.Ordinal)
				.ToList();

			if (normalized.Count == 0)
			{
				return;
			}

			var blobName = _config.WhitelistedPhoneNumbersBlobObjectName;
			if (string.IsNullOrWhiteSpace(blobName))
			{
				_logger.Warn("WhitelistedPhoneNumbersBlobObjectName is not configured. Skipping phone whitelist update.");
				return;
			}

			try
			{
				var existingContent = string.Empty;
				try
				{
					existingContent = await _blobProvider.GetBlobValue(blobName) ?? string.Empty;
				}
				catch (RequestFailedException)
				{
					// Blob may not exist yet; we'll create it
					existingContent = string.Empty;
				}

				var existing = existingContent
					.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
					.Select(x => x.Trim())
					.Where(x => x.Length > 0)
					.ToHashSet(StringComparer.Ordinal);

				var anyNew = false;
				foreach (var number in normalized)
				{
					if (!existing.Contains(number))
					{
						existing.Add(number);
						anyNew = true;
					}
				}

				if (!anyNew)
				{
					return;
				}

				var newContent = new StringBuilder();
				foreach (var number in existing.OrderBy(x => x, StringComparer.Ordinal))
				{
					newContent.AppendLine(number);
				}

				await _blobProvider.SetBlobValue(blobName, newContent.ToString());
			}
			catch (Exception e)
			{
				_logger.Error("Failed to update phone whitelist: " + e.Message);
				throw;
			}
		}
	}
}

