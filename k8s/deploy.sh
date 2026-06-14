#!/bin/bash
set -e

echo "==> Loading Docker images into Minikube..."
minikube image load snus-project-ingestion-service:latest
minikube image load snus-project-consensus-service:latest
minikube image load snus-project-notification-service:latest
minikube image load snus-project-ingress:latest

echo "==> Applying Kubernetes manifests..."
kubectl apply -f k8s/postgres-secret.yaml
kubectl apply -f k8s/postgres.yaml
kubectl apply -f k8s/ingestion-service.yaml
kubectl apply -f k8s/consensus-service.yaml
kubectl apply -f k8s/notification-service.yaml
kubectl apply -f k8s/ingress-service.yaml

echo "==> Waiting for pods to be ready..."
kubectl wait --for=condition=ready pod -l app=postgres --timeout=60s
kubectl wait --for=condition=ready pod -l app=ingestion-service --timeout=60s
kubectl wait --for=condition=ready pod -l app=notification-service --timeout=60s
kubectl wait --for=condition=ready pod -l app=ingress --timeout=60s

echo ""
echo "==> All services are up!"
echo "==> Access the system at: $(minikube service ingress --url)"