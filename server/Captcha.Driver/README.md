# SeliseBlocks.CaptchaDriver

[![NuGet](https://img.shields.io/nuget/v/SeliseBlocks.CaptchaDriver.svg)](https://www.nuget.org/packages/SeliseBlocks.CaptchaDriver)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet.svg)](https://dotnet.microsoft.com/)

A self-contained captcha driver for the **SELISE Blocks** platform. Provides the
full Submit + Verify flow for captcha challenges (blocks, reCAPTCHA, and hCaptcha)
along with configuration, processing, validation handlers, HTTP client, and
MongoDB-backed secret storage — all in a single NuGet package.

---

## Table of Contents

- [Features](#features)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Public API](#public-api)
- [Supported Providers](#supported-providers)
- [Requirements](#requirements)
- [License](#license)

---

## Features

- **Submit flow** — generate verification codes for captcha challenges.
- **Verify flow** — validate verification codes against the configured provider.
- **Multi-provider** — pluggable verification handlers for `bcaptcha` (Blocks),
  `recaptcha`, and `hcaptcha`.
- **MongoDB-backed secrets** — reads captcha configuration from the shared
  `Secrets` collection.
- **DI-friendly** — single extension method registers every dependency.
- **Validated inputs** — `FluentValidation` rules for submit requests.
- **Strongly-typed** — `System.Text.Json` deserialization for provider responses.

---

## Installation

Install via the .NET CLI:

```bash
dotnet add package SeliseBlocks.CaptchaDriver
```

Or via the NuGet Package Manager:

```powershell
Install-Package SeliseBlocks.CaptchaDriver
```

The package targets **.NET 10.0** and brings in its own transitive dependencies
(`FluentValidation`, `Microsoft.Extensions.DependencyInjection.Abstractions`,
`Microsoft.Extensions.Http`, `Microsoft.Extensions.Options.ConfigurationExtensions`,
`MongoDB.Driver`, `SeliseBlocks.Genesis`).

---

## Quick Start

### 1. Register services

```csharp
using Blocks.Extension.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.RegisterBlocksCaptchaService();
```

### 2. Inject and use

```csharp
using Blocks.CaptchaDriver;

public sealed class SignupService
{
    private readonly ICaptchaDriverService _captcha;

    public SignupService(ICaptchaDriverService captcha) => _captcha = captcha;

    public async Task<string> SubmitAsync(string captchaId, string hostName, CancellationToken ct = default)
    {
        var response = await _captcha.Submit(new SubmitCaptchaRequest
        {
            Id = captchaId,
            HostName = hostName
        });
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException("Captcha submission failed.");
        }
        return response.VerificationCode;
    }

    public async Task<bool> VerifyAsync(string verificationCode, string provider, CancellationToken ct = default)
    {
        var response = await _captcha.Verify(new VerifyCaptchaRequest
        {
            VerificationCode = verificationCode,
            ConfigurationName = provider
        });
        return response.IsSuccess && response.Verified;
    }
}
```

---

## Configuration

Captcha settings are read from a MongoDB `Secrets` collection document whose
`SecretKey` is `captcha`. Values live under `KeyValuePairs` (lookup is
case-insensitive).

### Required keys

| Key             | Type     | Description                                                    |
| --------------- | -------- | -------------------------------------------------------------- |
| `IsEnable`      | `bool`   | Whether captcha is enabled (`true`/`false`, case-insensitive). |
| `Provider`      | `string` | Provider name: `bcaptcha`, `recaptcha`, or `hcaptcha`.         |
| `CaptchaKey`    | `string` | Provider site/public key.                                      |
| `CaptchaSecret` | `string` | Provider secret key.                                          |

### Application configuration (appsettings.json)

The driver binds a `Captcha` configuration section. Override defaults per environment:

```json
{
  "Captcha": {
    "VerificationCodeTtlSeconds": 600,
    "RecaptchaVerificationUrl": "https://www.google.com/recaptcha/api/siteverify",
    "HcaptchaVerificationUrl": "https://api.hcaptcha.com/siteverify"
  }
}
```

| Key                            | Default                                       | Description                                                            |
| ------------------------------ | --------------------------------------------- | ---------------------------------------------------------------------- |
| `Captcha:VerificationCodeTtlSeconds` | `600`                                   | Time-to-live of verification codes in the cache (in seconds).           |
| `Captcha:RecaptchaVerificationUrl`   | `https://www.google.com/recaptcha/api/siteverify` | Google reCAPTCHA siteverify endpoint.                          |
| `Captcha:HcaptchaVerificationUrl`     | `https://api.hcaptcha.com/siteverify`      | hCaptcha siteverify endpoint.                                          |

### Example document

```json
{
  "SecretKey": "captcha",
  "KeyValuePairs": {
    "isEnable": "true",
    "provider": "recaptcha",
    "captchaKey": "6Lc...your-site-key",
    "captchaSecret": "6Lc...your-secret-key"
  }
}
```

> The legacy layout — `KeyValuePairs` dictionary with camelCase keys
> (`isEnable`, `provider`, `captchaKey`, `captchaSecret`) — is also supported.

### Provider-specific configuration

| Provider    | Extra config                                                                                            |
| ----------- | ------------------------------------------------------------------------------------------------------- |
| `bcaptcha`  | None. Hostname is recorded against the verification code in the cache.                                  |
| `recaptcha` | Optional DB override of the secret key via the `Secrets` document. Verification URL is `Captcha:RecaptchaVerificationUrl`. |
| `hcaptcha`  | Requires `Captcha:HcaptchaVerificationUrl` in configuration (defaults to `https://api.hcaptcha.com/siteverify`). |

---

## Public API

### Entry point

| Type                     | Description                                                |
| ------------------------ | ---------------------------------------------------------- |
| `ICaptchaDriverService`  | Public interface exposed to consumers (`Submit`/`Verify`). |
| `CaptchaDriverService`   | Default implementation that delegates to `ICaptchaService`. |
| `CaptchaDriverServiceExtension` | DI extension: `IServiceCollection.RegisterBlocksCaptchaService()`. |

### Request / response models

| Type                              | Purpose                                     |
| --------------------------------- | ------------------------------------------- |
| `SubmitCaptchaRequest`            | Payload for the Submit flow (`Id`, `Value`, `HostName`). |
| `SubmitCaptchaRequestResponse`    | Contains `VerificationCode` and errors.      |
| `VerifyCaptchaRequest`            | Payload for the Verify flow (`VerificationCode`, `ConfigurationName`). |
| `VerifyCaptchaRequestResponse`    | Contains `Verified`, `HostName`, and errors. |

### Core services (advanced consumers)

| Type                                  | Purpose                                                    |
| ------------------------------------- | ---------------------------------------------------------- |
| `ICaptchaService` / `CaptchaService`  | Orchestrates the Submit and Verify flows.                  |
| `ICaptchaProcessor` / `CaptchaProcessor` | Generates verification codes and dispatches to providers. |
| `SubmitCaptchaCommandValidator`       | `FluentValidation` rule for `SubmitCaptchaRequest`.        |
| `ICaptchaConfigurationService`       | Reads captcha configuration by name or by enabled state.   |
| `ICaptchaConfigurationRepository`    | MongoDB-backed repository for the `Secrets` collection.    |
| `CaptchaConfigurationMapping`        | Static helper mapping a `Secret` document to a config.    |

### Verification providers

| Type                                       | Purpose                                                  |
| ------------------------------------------ | -------------------------------------------------------- |
| `ICaptchaVerificationServiceProvider`      | Resolves the right handler by provider name.             |
| `BlocksCaptchaVerificationService`         | `bcaptcha` — verifies via cached verification code.      |
| `ReCaptchaVerificationService`             | `recaptcha` — verifies via Google siteverify endpoint.   |
| `HCaptchaVerificationService`             | `hcaptcha` — verifies via hCaptcha siteverify endpoint.  |
| `RecaptchaConfigFactory` / `IRecaptchaConfigFactory` | Picks DB or local config for reCAPTCHA.         |

---

## Supported Providers

| Provider     | Behavior                                                                                  |
| ------------ | ----------------------------------------------------------------------------------------- |
| `bcaptcha`   | Default. The verification code is bound to a hostname in the cache and consumed on Verify. |
| `recaptcha`  | Calls Google's `siteverify` endpoint using the configured secret key.                      |
| `hcaptcha`   | Calls hCaptcha's `siteverify` endpoint using the configured secret key.                    |

The provider is selected per request via `VerifyCaptchaRequest.ConfigurationName`
or, when omitted, defaults to `bcaptcha`.

---

## Requirements

- **.NET 10.0** or later
- **MongoDB** instance accessible via `IDbContextProvider` (provided by `SeliseBlocks.Genesis`)
- **Cache** (e.g. Redis) accessible via `ICacheClient` (provided by `SeliseBlocks.Genesis`)
- `IConfiguration` bound if using `recaptcha` / `hcaptcha` providers

---

## License

This package is licensed under the **MIT License**.

© SELISE Digital Platforms. All rights reserved.