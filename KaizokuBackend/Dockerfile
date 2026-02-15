
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine3.23-extra
ARG TARGETPLATFORM

RUN apt-get update && \
    apt-get -y install --no-install-recommends -y gettext-base unzip tini libicu-dev gosu cron libxss1 libxext6 libxrender1 libxcomposite1 libxdamage1 libxkbcommon0 libxtst6 \
    libgtk-3-0 libjogl2-jni libgluegen2-jni libglib2.0-0t64 libnss3 libdbus-1-3 libpango-1.0-0 libcairo2 libasound2t64 \
    libatk-bridge2.0-0t64 libcups2t64 libdrm2 libgbm1 xvfb 

# .X11-unix must be created by root
# Ubuntu exposes libgluegen_rt.so as libgluegen2_rt.so for some reason, so rename it
# JCEF (or Java?) also does not search /usr/lib/jni, so copy them over into one it will search
RUN if command -v Xvfb; then \
      mkdir /tmp/.X11-unix && chmod 1777 /tmp/.X11-unix && \
      cp /usr/lib/jni/libgluegen2_rt.so libgluegen_rt.so && \
      cp /usr/lib/jni/*.so ./; \
    fi

ENV HOME=/config/mihon
WORKDIR /app
COPY ./bin/$TARGETPLATFORM/ .
COPY ./scripts/entrypoint.sh /app/entrypoint.sh
EXPOSE 9833
ENTRYPOINT ["tini", "--"]
CMD ["/app/entrypoint.sh"]