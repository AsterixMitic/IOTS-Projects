use thiserror::Error;

#[derive(Error, Debug)]
pub enum ServiceError {
    #[error("Database error: {0}")]
    DatabaseError(String),

    #[error("Invalid argument: {0}")]
    InvalidArgument(String),

    #[error("Not found")]
    NotFound,

    #[error("Duplicate sensor code after normalization")]
    DuplicateSensorCode,

    #[error("Internal server error")]
    InternalServerError,
}

pub type ServiceResult<T> = Result<T, ServiceError>;
