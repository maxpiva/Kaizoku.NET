package xyz.nulldev.ts.config

/*
 * Copyright (C) Contributors to the Suwayomi project
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

import com.typesafe.config.Config
import xyz.nulldev.ts.config.logging.AndroidCompatLogBridgeProxy

const val BASE_LOGGER_NAME = "_BaseLogger"

fun initLoggerConfig(
    appRootPath: String,
    maxFiles: Int,
    maxFileSize: String,
    maxTotalSize: String,
) {
    // On the ExtensionBridge host we forward all logs to the .NET sink, so
    // file appenders and logback configuration are intentionally disabled.
    AndroidCompatLogBridgeProxy.setMinimumLevelName("INFO")
}

fun updateFileAppender(
    maxFiles: Int,
    maxFileSize: String,
    maxTotalSize: String,
) {
    // No-op: log rotation is handled by the .NET host.
}

fun setLogLevelFor(
    name: String,
    levelName: String,
) {
    if (name != BASE_LOGGER_NAME) {
        return
    }

    val normalizedLevel = levelName.uppercase()
    AndroidCompatLogBridgeProxy.setMinimumLevelName(normalizedLevel)
}

fun debugLogsEnabled(config: Config) =
    System.getProperty("suwayomi.tachidesk.config.server.debugLogsEnabled", config.getString("server.debugLogsEnabled")).toBoolean()
