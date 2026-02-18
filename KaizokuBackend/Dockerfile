
FROM eclipse-temurin:21.0.7_6-jdk-noble AS build
ARG TARGETPLATFORM

# build abort handler
RUN cd /tmp && \
      curl "https://raw.githubusercontent.com/Suwayomi/Suwayomi-Server/refs/heads/master/scripts/resources/catch_abort.c" -O && \
      apt-get update && \
      apt-get -y install gcc && \
      gcc -fPIC -I$JAVA_HOME/include -I$JAVA_HOME/include/linux -shared catch_abort.c -lpthread -o /opt/catch_abort.so && \
      rm -f catch_abort.c && \
      apt-get -y purge gcc --auto-remove && \
      apt-get clean && \
      rm -rf /var/lib/apt/lists/* || exit 1; 

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM eclipse-temurin:21.0.7_6-jre-noble
ARG TARGETPLATFORM

# Install envsubst from GNU's gettext project
RUN apt-get update && \
    apt-get -y install gettext-base unzip tini libicu-dev gosu cron && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

COPY ./scripts/kcef_download.sh /root/kcef_download.sh
RUN chmod +x /root/kcef_download.sh

# install CEF dependencies
RUN if ([ "$TARGETPLATFORM" = "linux/amd64" ] || [ "$TARGETPLATFORM" = "linux/arm64" ]); then \
      apt-get update && \
      apt-get -y install --no-install-recommends -y libxss1 libxext6 libxrender1 libxcomposite1 libxdamage1 libxkbcommon0 libxtst6 \
          libjogl2-jni libgluegen2-jni libglib2.0-0t64 libnss3 libdbus-1-3 libpango-1.0-0 libcairo2 libasound2t64 \
          libatk-bridge2.0-0t64 libcups2t64 libdrm2 libgbm1 xvfb \
          curl jq gawk findutils && \
      /root/kcef_download.sh "https://api.github.com/repos/JetBrains/JetBrainsRuntime/releases/latest" "$TARGETPLATFORM" && \
      apt-get clean && \
      rm -rf /var/lib/apt/lists/* || exit 1; \
    fi
# .X11-unix must be created by root
# Ubuntu exposes libgluegen_rt.so as libgluegen2_rt.so for some reason, so rename it
# JCEF (or Java?) also does not search /usr/lib/jni, so copy them over into one it will search
RUN if command -v Xvfb; then \
      mkdir /tmp/.X11-unix && chmod 1777 /tmp/.X11-unix && \
      cp /usr/lib/jni/libgluegen2_rt.so libgluegen_rt.so && \
      cp /usr/lib/jni/*.so ./; \
    fi


COPY --from=build /opt/*.so /opt/

ENV HOME=/config/Suwayomi
WORKDIR /app
COPY ./bin/$TARGETPLATFORM/ .
COPY ./scripts/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh
EXPOSE 9833
EXPOSE 4567
ENTRYPOINT ["tini", "--"]
CMD ["/app/entrypoint.sh"]