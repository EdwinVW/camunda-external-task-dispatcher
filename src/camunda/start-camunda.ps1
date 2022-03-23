docker volume create --name=camundadata 
docker run -d --name camunda -p 8080:8080 --mount source=camundadata,target=/camunda edwinvw/camunda-bpm-platform:1.0