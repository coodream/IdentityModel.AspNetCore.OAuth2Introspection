﻿// Copyright (c) Dominick Baier & Brock Allen. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace IdentityModel.AspNetCore.OAuth2Introspection
{
    internal static class CacheExtensions
    {
        internal static JsonSerializerSettings _settings;

        static CacheExtensions()
        {
            _settings = new JsonSerializerSettings();
            _settings.Converters.Add(new ClaimConverter());
        }

        public static async Task<IEnumerable<Claim>> GetClaimsAsync(this IDistributedCache cache, string token)
        {
            var bytes = await cache.GetAsync(token);

            if (bytes == null)
            {
                return null;
            }

            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<IEnumerable<Claim>>(json, _settings);
        }

        public static async Task SetClaimsAsync(this IDistributedCache cache, string token, IEnumerable<Claim> claims, TimeSpan duration, ILogger logger)
        {
            var expClaim = claims.FirstOrDefault(c => c.Type == JwtClaimTypes.Expiration);
            if (expClaim == null)
            {
                logger.LogWarning("No exp claim found on introspection response, can't cache.");
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var expiration = long.Parse(expClaim.Value).ToDateTimeOffsetFromEpoch();
            logger.LogDebug("Token will expire in {expiration}", expiration);
            

            if (expiration <= now)
            {
                return;
            }

            DateTimeOffset absoluteLifetime;
            if (expiration <= now.Add(duration))
            {
                absoluteLifetime = expiration;
            }
            else
            {
                absoluteLifetime = now.Add(duration);
            }

            var json = JsonConvert.SerializeObject(claims, _settings);
            var bytes = Encoding.UTF8.GetBytes(json);

            logger.LogDebug("Setting cache item expiration to {expiration}", absoluteLifetime);
            await cache.SetAsync(token, bytes, new DistributedCacheEntryOptions { AbsoluteExpiration = absoluteLifetime });
        }
    }
}