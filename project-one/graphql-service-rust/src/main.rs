use std::sync::Arc;

use actix_web::{web, App, HttpResponse, HttpServer};
use async_graphql::http::{playground_source, GraphQLPlaygroundConfig};
use async_graphql::{EmptySubscription, Schema};
use async_graphql_actix_web::{GraphQLRequest, GraphQLResponse};

mod db;
mod errors;
mod models;
mod schema;

use db::DbPool;
use schema::{AppSchema, MutationRoot, QueryRoot};

/// POST /graphql — executes a real GraphQL request. async-graphql parses the
/// query + variables and evaluates only the selected fields, so field
/// selection (and therefore over-fetch avoidance) works out of the box.
async fn graphql_handler(schema: web::Data<AppSchema>, req: GraphQLRequest) -> GraphQLResponse {
    schema.execute(req.into_inner()).await.into()
}

/// GET /graphql (and /) — interactive GraphQL Playground for exploring the schema.
async fn graphql_playground() -> HttpResponse {
    HttpResponse::Ok()
        .content_type("text/html; charset=utf-8")
        .body(playground_source(GraphQLPlaygroundConfig::new("/graphql")))
}

#[actix_web::main]
async fn main() -> std::io::Result<()> {
    tracing_subscriber::fmt::init();
    dotenv::dotenv().ok();

    let db_url = std::env::var("DATABASE_URL").unwrap_or_else(|_| {
        let user = std::env::var("POSTGRES_USER").unwrap_or_else(|_| "root".to_string());
        let password = std::env::var("POSTGRES_PASSWORD").unwrap_or_else(|_| "root".to_string());
        let db = std::env::var("POSTGRES_DB").unwrap_or_else(|_| "iot_db".to_string());
        let host = std::env::var("POSTGRES_HOST").unwrap_or_else(|_| "localhost".to_string());
        format!("postgresql://{}:{}@{}/{}", user, password, host, db)
    });

    tracing::info!("Connecting to database");
    let pool = DbPool::new(&db_url)
        .await
        .expect("Failed to create database pool");
    tracing::info!("Database pool created successfully");

    let schema = Schema::build(QueryRoot, MutationRoot, EmptySubscription)
        .data(Arc::new(pool))
        .finish();

    tracing::info!("Starting GraphQL service on 0.0.0.0:8000");

    HttpServer::new(move || {
        App::new()
            .app_data(web::Data::new(schema.clone()))
            .route("/graphql", web::post().to(graphql_handler))
            .route("/graphql", web::get().to(graphql_playground))
            .route("/", web::get().to(graphql_playground))
    })
    .bind("0.0.0.0:8000")?
    .run()
    .await
}
